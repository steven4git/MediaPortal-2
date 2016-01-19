﻿#region Copyright (C) 2007-2012 Team MediaPortal

/*
    Copyright (C) 2007-2012 Team MediaPortal
    http://www.team-mediaportal.com

    This file is part of MediaPortal 2

    MediaPortal 2 is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    MediaPortal 2 is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with MediaPortal 2. If not, see <http://www.gnu.org/licenses/>.
*/

#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MediaPortal.Common.MediaManagement;
using MediaPortal.Common.ResourceAccess;
using MediaPortal.Common.MediaManagement.DefaultItemAspects;
using System.Diagnostics;
using MediaPortal.Common;
using MediaPortal.Common.Logging;
using System.IO;
using System.Threading;
using System.Globalization;
using MediaPortal.Plugins.Transcoding.Service;
using System.Drawing;
using MediaPortal.Plugins.MP2Extended;
using MediaPortal.Plugins.MP2Extended.Metadata;
using MediaPortal.Plugins.Transcoding.Aspects;
using MediaPortal.Plugins.MP2Extended.ResourceAccess.WSS;
using MediaPortal.Plugins.SlimTv.Interfaces.LiveTvMediaItem;
using MediaPortal.Plugins.SlimTv.Interfaces.ResourceProvider;

namespace MediaPortal.Plugins.MP2Extended.ResourceAccess.WSS.Profiles
{
  public class ProfileMediaItem
  {
    private List<Guid> _streams = new List<Guid>();
    private string _clientId = null;

    public ProfileMediaItem(string clientId, MediaItem item, EndPointSettings client, bool live)
    {
      _clientId = clientId;

      Client = client;
      MediaSource = item;
      LastUpdated = DateTime.Now;
      TranscodingParameter = null;
      IsSegmented = false;
      IsLive = live;
      MetadataContainer info = null;
      bool bForceTranscoding = false;
      if (item.Aspects.ContainsKey(AudioAspect.ASPECT_ID))
      {
        IsAudio = true;
        if (item.Aspects.ContainsKey(TranscodeItemAudioAspect.ASPECT_ID) == false)
        {
          if (item.Aspects[MediaAspect.ASPECT_ID][MediaAspect.ATTR_MIME_TYPE].ToString() == LiveTvMediaItem.MIME_TYPE_RADIO)
          {
            info = ParseLiveMedia(item);
            bForceTranscoding = true;
          }
          else
          {
            Logger.Warn("Mediaitem {0} contains no transcoding audio information", item.MediaItemId);
            return;
          }
        }
        else
        {
          info = WebAudioMetadata.ParseMediaItem(item);
        }
      }
      else if (item.Aspects.ContainsKey(ImageAspect.ASPECT_ID))
      {
        IsImage = true;
        if (item.Aspects.ContainsKey(TranscodeItemImageAspect.ASPECT_ID) == false)
        {
          Logger.Warn("Mediaitem {0} contains no transcoding image information", item.MediaItemId);
          return;
        }
        else
        {
          info = WebImageMetadata.ParseMediaItem(item);
        }
      }
      else if (item.Aspects.ContainsKey(VideoAspect.ASPECT_ID))
      {
        IsVideo = true;
        if (item.Aspects.ContainsKey(TranscodeItemVideoAspect.ASPECT_ID) == false)
        {
          if (item.Aspects[MediaAspect.ASPECT_ID][MediaAspect.ATTR_MIME_TYPE].ToString() == LiveTvMediaItem.MIME_TYPE_TV)
          {
            info = ParseLiveMedia(item);
            bForceTranscoding = true;
          }
          else
          {
            Logger.Warn("Mediaitem {0} contains no transcoding video information", item.MediaItemId);
            return;
          }
        }
        else
        {
          info = WebVideoMetadata.ParseMediaItem(item);
        }
      }
      else
      {
        Logger.Warn("Mediaitem {0} contains no required aspect information", item.MediaItemId);
        return;
      }

      if (MP2Extended.Settings.TranscodingAllowed == true)
      {
        if (IsAudio)
        {
          AudioMatch srcAudio;
          AudioTranscodingTarget dstAudio = client.Profile.GetMatchingAudioTranscoding(info, out srcAudio);
          if (dstAudio != null && srcAudio.MatchedAudioSource.Matches(dstAudio.Target) == false)
          {
            AudioTranscoding audio = new AudioTranscoding();
            if(info.Metadata.AudioContainerType != AudioContainer.Unknown)
            {
              audio.SourceAudioContainer = info.Metadata.AudioContainerType;
            }
            if(info.Audio[srcAudio.MatchedAudioStream].Bitrate > 0)
            {
              audio.SourceAudioBitrate = info.Audio[srcAudio.MatchedAudioStream].Bitrate;
            }
            if(info.Audio[srcAudio.MatchedAudioStream].Frequency > 0)
            {
              audio.SourceAudioFrequency = info.Audio[srcAudio.MatchedAudioStream].Frequency;
            }
            if(info.Audio[srcAudio.MatchedAudioStream].Channels > 0)
            {
              audio.SourceAudioChannels = info.Audio[srcAudio.MatchedAudioStream].Channels;
            }
            if(info.Audio[srcAudio.MatchedAudioStream].Codec != AudioCodec.Unknown)
            {
              audio.SourceAudioCodec = info.Audio[srcAudio.MatchedAudioStream].Codec;
            }
            if(info.Metadata.Duration > 0)
            {
              audio.SourceDuration = TimeSpan.FromSeconds(info.Metadata.Duration);
            }
            if(info.Metadata.Source != null)
            {
              audio.SourceMedia = info.Metadata.Source;
            }
            
            audio.TargetAudioBitrate = client.Profile.Settings.Audio.DefaultBitrate;
            if(dstAudio.Target.Bitrate > 0)
            {
              audio.TargetAudioBitrate = dstAudio.Target.Bitrate;
            }
            if(dstAudio.Target.AudioContainerType != AudioContainer.Unknown)
            {
              audio.TargetAudioContainer = dstAudio.Target.AudioContainerType;
            }
            if(dstAudio.Target.Frequency > 0)
            {
              audio.TargetAudioFrequency = dstAudio.Target.Frequency;
            }
            audio.TargetForceAudioStereo = client.Profile.Settings.Audio.DefaultStereo;
            if(dstAudio.Target.ForceStereo)
            {
              audio.TargetForceAudioStereo = dstAudio.Target.ForceStereo;
            }

            audio.TargetCoder = client.Profile.Settings.Audio.CoderType;
            audio.TargetIsLive = live;

            audio.TranscoderBinPath = dstAudio.TranscoderBinPath;
            audio.TranscoderArguments = dstAudio.TranscoderArguments;
            audio.TranscodeId = MediaSource.MediaItemId.ToString()  + "_" +  Client.Profile.ID;
            TranscodingParameter = audio;
          }
        }
        else if (IsImage)
        {
          ImageMatch srcImage;
          ImageTranscodingTarget dstImage = client.Profile.GetMatchingImageTranscoding(info, out srcImage);
          if (dstImage != null && srcImage.MatchedImageSource.Matches(dstImage.Target) == false)
          {
            ImageTranscoding image = new ImageTranscoding();
            if (info.Metadata.ImageContainerType != ImageContainer.Unknown)
            {
              image.SourceImageCodec = info.Metadata.ImageContainerType;
            }
            if (info.Image.Height > 0)
            {
              image.SourceHeight = info.Image.Height;
            }
            if (info.Image.Width > 0)
            {
              image.SourceWidth = info.Image.Width;
            }
            if (info.Image.Orientation > 0)
            {
              image.SourceOrientation = info.Image.Orientation;
            }
            if (info.Image.PixelFormatType != PixelFormat.Unknown)
            {
              image.SourcePixelFormat = info.Image.PixelFormatType;
            }
            if (info.Metadata.Source != null)
            {
              image.SourceMedia = info.Metadata.Source;
            }

            if (dstImage.Target.PixelFormatType > 0)
            {
              image.TargetPixelFormat = dstImage.Target.PixelFormatType;
            }
            if (dstImage.Target.ImageContainerType != ImageContainer.Unknown)
            {
              image.TargetImageCodec = dstImage.Target.ImageContainerType;
            }
            image.TargetImageQuality = client.Profile.Settings.Images.Quality;
            if (dstImage.Target.QualityType != QualityMode.Default)
            {
              image.TargetImageQuality = dstImage.Target.QualityType;
            }

            image.TargetImageQualityFactor = client.Profile.Settings.Video.QualityFactor;

            image.TargetAutoRotate = client.Profile.Settings.Images.AutoRotate;
            image.TargetCoder = client.Profile.Settings.Images.CoderType;
            image.TargetHeight = client.Profile.Settings.Images.MaxHeight;
            image.TargetWidth = client.Profile.Settings.Images.MaxWidth;

            image.TranscoderBinPath = dstImage.TranscoderBinPath;
            image.TranscoderArguments = dstImage.TranscoderArguments;

            image.TranscodeId = MediaSource.MediaItemId.ToString() + "_" + Client.Profile.ID;
            TranscodingParameter = image;
          }
        }
        else if (IsVideo)
        {
          VideoMatch srcVideo;
          VideoTranscodingTarget dstVideo = client.Profile.GetMatchingVideoTranscoding(info, client, out srcVideo);
          if (dstVideo != null && srcVideo.MatchedVideoSource.Matches(dstVideo.Target) == false)
          {
            VideoTranscoding video = new VideoTranscoding();
            video.SourceAudioStreamIndex = info.Audio[srcVideo.MatchedAudioStream].StreamIndex;
            video.SourceVideoStreamIndex = info.Video.StreamIndex;
            if (info.Metadata.VideoContainerType != VideoContainer.Unknown)
            {
              video.SourceVideoContainer = info.Metadata.VideoContainerType;
            }
            if (info.Audio[srcVideo.MatchedAudioStream].Bitrate > 0)
            {
              video.SourceAudioBitrate = info.Audio[srcVideo.MatchedAudioStream].Bitrate;
            }
            if (info.Audio[srcVideo.MatchedAudioStream].Frequency > 0)
            {
              video.SourceAudioFrequency = info.Audio[srcVideo.MatchedAudioStream].Frequency;
            }
            if (info.Audio[srcVideo.MatchedAudioStream].Channels > 0)
            {
              video.SourceAudioChannels = info.Audio[srcVideo.MatchedAudioStream].Channels;
            }
            if (info.Audio[srcVideo.MatchedAudioStream].Codec != AudioCodec.Unknown)
            {
              video.SourceAudioCodec = info.Audio[srcVideo.MatchedAudioStream].Codec;
            }
            video.SourceSubtitles = new List<SubtitleStream>(info.Subtitles);

            if (info.Video.Bitrate > 0)
            {
              video.SourceVideoBitrate = info.Video.Bitrate;
            }
            if (info.Video.Framerate > 0)
            {
              video.SourceFrameRate = info.Video.Framerate;
            }
            if (info.Video.PixelFormatType != PixelFormat.Unknown)
            {
              video.SourcePixelFormat = info.Video.PixelFormatType;
            }
            if (info.Video.AspectRatio > 0)
            {
              video.SourceVideoAspectRatio = info.Video.AspectRatio;
            }
            if (info.Video.Codec != VideoCodec.Unknown)
            {
              video.SourceVideoCodec = info.Video.Codec;
            }
            if (info.Video.Height > 0)
            {
              video.SourceVideoHeight = info.Video.Height;
            }
            if (info.Video.Width > 0)
            {
              video.SourceVideoWidth = info.Video.Width;
            }
            if (info.Video.PixelAspectRatio > 0)
            {
              video.SourceVideoPixelAspectRatio = info.Video.PixelAspectRatio;
            }

            if (info.Metadata.Duration > 0)
            {
              video.SourceDuration = TimeSpan.FromSeconds(info.Metadata.Duration);
            }
            if (info.Metadata.Source != null)
            {
              video.SourceMedia = info.Metadata.Source;
            }

            if (dstVideo.Target.VideoContainerType != VideoContainer.Unknown)
            {
              video.TargetVideoContainer = dstVideo.Target.VideoContainerType;
            }
            if (video.TargetVideoContainer == VideoContainer.Hls)
            {
              IsSegmented = true;
            }

            if (dstVideo.Target.Movflags != null)
            {
              video.Movflags = dstVideo.Target.Movflags;
            }

            video.TargetAudioBitrate = client.Profile.Settings.Audio.DefaultBitrate;
            if (dstVideo.Target.AudioBitrate > 0)
            {
              video.TargetAudioBitrate = dstVideo.Target.AudioBitrate;
            }
            if (dstVideo.Target.AudioFrequency > 0)
            {
              video.TargetAudioFrequency = dstVideo.Target.AudioFrequency;
            }
            if (dstVideo.Target.AudioCodecType != AudioCodec.Unknown)
            {
              video.TargetAudioCodec = dstVideo.Target.AudioCodecType;
            }
            video.TargetForceAudioStereo = client.Profile.Settings.Audio.DefaultStereo;
            if (dstVideo.Target.ForceStereo)
            {
              video.TargetForceAudioStereo = dstVideo.Target.ForceStereo;
            }

            video.TargetVideoQuality = client.Profile.Settings.Video.Quality;
            if (dstVideo.Target.QualityType != QualityMode.Default)
            {
              video.TargetVideoQuality = dstVideo.Target.QualityType;
            }
            if (dstVideo.Target.PixelFormatType != PixelFormat.Unknown)
            {
              video.TargetPixelFormat = dstVideo.Target.PixelFormatType;
            }
            if (dstVideo.Target.AspectRatio > 0)
            {
              video.TargetVideoAspectRatio = dstVideo.Target.AspectRatio;
            }
            if (dstVideo.Target.MaxVideoBitrate > 0)
            {
              video.TargetVideoBitrate = dstVideo.Target.MaxVideoBitrate;
            }
            if (dstVideo.Target.VideoCodecType != VideoCodec.Unknown)
            {
              video.TargetVideoCodec = dstVideo.Target.VideoCodecType;
            }
            video.TargetVideoMaxHeight = client.Profile.Settings.Video.MaxHeight;
            if (dstVideo.Target.MaxVideoHeight > 0)
            {
              video.TargetVideoMaxHeight = dstVideo.Target.MaxVideoHeight;
            }
            video.TargetForceVideoTranscoding = dstVideo.Target.ForceVideoTranscoding;

            if (dstVideo.Target.VideoCodecType == VideoCodec.Mpeg2)
            {
              video.TargetQualityFactor = client.Profile.Settings.Video.H262QualityFactor;
              video.TargetProfile = client.Profile.Settings.Video.H262TargetProfile;
              video.TargetPreset = client.Profile.Settings.Video.H262TargetPreset;
            }
            else if (dstVideo.Target.VideoCodecType == VideoCodec.H264)
            {
              video.TargetQualityFactor = client.Profile.Settings.Video.H264QualityFactor;
              video.TargetLevel = client.Profile.Settings.Video.H264Level;
              if (dstVideo.Target.LevelMinimum > 0)
              {
                video.TargetLevel = dstVideo.Target.LevelMinimum;
              }
              video.TargetProfile = client.Profile.Settings.Video.H264TargetProfile;
              if (dstVideo.Target.EncodingProfileType != EncodingProfile.Unknown)
              {
                video.TargetProfile = dstVideo.Target.EncodingProfileType;
              }
              video.TargetPreset = client.Profile.Settings.Video.H264TargetPreset;
              if (dstVideo.Target.TargetPresetType != EncodingPreset.Default)
              {
                video.TargetPreset = dstVideo.Target.TargetPresetType;
              }
            }
            else if (dstVideo.Target.VideoCodecType == VideoCodec.H265)
            {
              video.TargetQualityFactor = client.Profile.Settings.Video.H265QualityFactor;
              video.TargetLevel = client.Profile.Settings.Video.H265Level;
              if (dstVideo.Target.LevelMinimum > 0)
              {
                video.TargetLevel = dstVideo.Target.LevelMinimum;
              }
              video.TargetProfile = client.Profile.Settings.Video.H265TargetProfile;
              if (dstVideo.Target.EncodingProfileType != EncodingProfile.Unknown)
              {
                video.TargetProfile = dstVideo.Target.EncodingProfileType;
              }
              video.TargetPreset = client.Profile.Settings.Video.H265TargetPreset;
              if (dstVideo.Target.TargetPresetType != EncodingPreset.Default)
              {
                video.TargetPreset = dstVideo.Target.TargetPresetType;
              }
            }

            video.TargetVideoQualityFactor = client.Profile.Settings.Video.QualityFactor;
            video.TargetCoder = client.Profile.Settings.Video.CoderType;
            video.TargetIsLive = live;

            video.TargetSubtitleSupport = client.Profile.Settings.Subtitles.SubtitleMode;
            if (MP2Extended.Settings.HardcodedSubtitlesAllowed == false && client.Profile.Settings.Subtitles.SubtitleMode == SubtitleSupport.HardCoded)
            {
              video.TargetSubtitleSupport = SubtitleSupport.None;
            }

            video.TranscoderBinPath = dstVideo.TranscoderBinPath;
            video.TranscoderArguments = dstVideo.TranscoderArguments;
            video.TranscodeId = MediaSource.MediaItemId.ToString() + "_" + Client.Profile.ID;
            TranscodingParameter = video;
          }
        }
      }
      if (bForceTranscoding == true && TranscodingParameter == null)
      {
        if (IsVideo) TranscodingParameter = LiveVideoTranscoding(info, client);
        else if (IsAudio) TranscodingParameter = LiveAudioTranscoding(info, client);
      }

      if(IsLive == false && IsVideo && TranscodingParameter == null)
      {
        VideoTranscoding subtitle = new VideoTranscoding();
        subtitle.SourceMedia = info.Metadata.Source;
        subtitle.TargetSubtitleSupport = client.Profile.Settings.Subtitles.SubtitleMode;
        subtitle.SourceSubtitles.AddRange(info.Subtitles);
        if (MP2Extended.Settings.HardcodedSubtitlesAllowed == false && client.Profile.Settings.Subtitles.SubtitleMode == SubtitleSupport.HardCoded)
        {
          subtitle.TargetSubtitleSupport = SubtitleSupport.None;
        }
        subtitle.TranscodeId = MediaSource.MediaItemId.ToString() + "_" + Client.Profile.ID;
        subtitle.TargetIsLive = live;
        SubtitleTranscodingParameter = subtitle;
      }
      if (IsLive && TranscodingParameter != null)
      {
        //Ensure uniqueness of stream
        TranscodingParameter.TranscodeId = Guid.NewGuid().ToString() + "_" + Client.Profile.ID;
      }

      AssignWebMetadata(info);
    }

    public VideoTranscoding LiveVideoTranscoding(MetadataContainer info, EndPointSettings client)
    {
      int iMatchedAudioStream = 0;
      if (string.IsNullOrEmpty(client.PreferredAudioLanguages) == false)
      {
        List<string> valuesLangs = new List<string>(client.PreferredAudioLanguages.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
        int currentPriority = -1;
        for (int iAudio = 0; iAudio < info.Audio.Count; iAudio++)
        {
          for (int iPriority = 0; iPriority < valuesLangs.Count; iPriority++)
          {
            if (valuesLangs[iPriority].Equals(info.Audio[iAudio].Language, StringComparison.InvariantCultureIgnoreCase) == true)
            {
              if (currentPriority == -1 || iPriority < currentPriority)
              {
                currentPriority = iPriority;
                iMatchedAudioStream = iAudio;
              }
            }
          }
        }
      }

      VideoTranscoding video = new VideoTranscoding();
      video.SourceAudioStreamIndex = info.Audio[iMatchedAudioStream].StreamIndex;
      video.SourceVideoStreamIndex = info.Video.StreamIndex;
      if (info.Metadata.VideoContainerType != VideoContainer.Unknown)
      {
        video.SourceVideoContainer = info.Metadata.VideoContainerType;
      }
      if (info.Audio[iMatchedAudioStream].Bitrate > 0)
      {
        video.SourceAudioBitrate = info.Audio[iMatchedAudioStream].Bitrate;
      }
      if (info.Audio[iMatchedAudioStream].Frequency > 0)
      {
        video.SourceAudioFrequency = info.Audio[iMatchedAudioStream].Frequency;
      }
      if (info.Audio[iMatchedAudioStream].Channels > 0)
      {
        video.SourceAudioChannels = info.Audio[iMatchedAudioStream].Channels;
      }
      if (info.Audio[iMatchedAudioStream].Codec != AudioCodec.Unknown)
      {
        video.SourceAudioCodec = info.Audio[iMatchedAudioStream].Codec;
      }
      video.SourceSubtitles = new List<SubtitleStream>(info.Subtitles);

      if (info.Video.Bitrate > 0)
      {
        video.SourceVideoBitrate = info.Video.Bitrate;
      }
      if (info.Video.Framerate > 0)
      {
        video.SourceFrameRate = info.Video.Framerate;
      }
      if (info.Video.PixelFormatType != PixelFormat.Unknown)
      {
        video.SourcePixelFormat = info.Video.PixelFormatType;
      }
      if (info.Video.AspectRatio > 0)
      {
        video.SourceVideoAspectRatio = info.Video.AspectRatio;
      }
      if (info.Video.Codec != VideoCodec.Unknown)
      {
        video.SourceVideoCodec = info.Video.Codec;
      }
      if (info.Video.Height > 0)
      {
        video.SourceVideoHeight = info.Video.Height;
      }
      if (info.Video.Width > 0)
      {
        video.SourceVideoWidth = info.Video.Width;
      }
      if (info.Video.PixelAspectRatio > 0)
      {
        video.SourceVideoPixelAspectRatio = info.Video.PixelAspectRatio;
      }
      //if (info.Metadata.Duration > 0)
      //{
      //  video.SourceDuration = TimeSpan.FromSeconds(info.Metadata.Duration);
      //}
      if (info.Metadata.Source != null)
      {
        video.SourceMedia = info.Metadata.Source;
      }

      video.TargetVideoContainer = video.SourceVideoContainer;
      if (video.TargetVideoContainer == VideoContainer.Hls)
      {
        IsSegmented = true;
      }
      video.TargetAudioCodec = video.SourceAudioCodec;
      video.TargetVideoCodec = video.SourceVideoCodec;
      video.TargetForceVideoCopy = true;
      video.TargetForceAudioCopy = true;

      video.TargetIsLive = true;
      video.TargetSubtitleSupport = SubtitleSupport.None;
      video.TranscodeId = Guid.Empty.ToString() + "_" + Client.Profile.ID;
      return video;
    }

    public AudioTranscoding LiveAudioTranscoding(MetadataContainer info, EndPointSettings client)
    {
      int iMatchedAudioStream = 0;

      AudioTranscoding audio = new AudioTranscoding();
      if (info.Metadata.AudioContainerType != AudioContainer.Unknown)
      {
        audio.SourceAudioContainer = info.Metadata.AudioContainerType;
      }
      if (info.Audio[iMatchedAudioStream].Bitrate > 0)
      {
        audio.SourceAudioBitrate = info.Audio[iMatchedAudioStream].Bitrate;
      }
      if (info.Audio[iMatchedAudioStream].Frequency > 0)
      {
        audio.SourceAudioFrequency = info.Audio[iMatchedAudioStream].Frequency;
      }
      if (info.Audio[iMatchedAudioStream].Channels > 0)
      {
        audio.SourceAudioChannels = info.Audio[iMatchedAudioStream].Channels;
      }
      if (info.Audio[iMatchedAudioStream].Codec != AudioCodec.Unknown)
      {
        audio.SourceAudioCodec = info.Audio[iMatchedAudioStream].Codec;
      }
      //if (info.Metadata.Duration > 0)
      //{
      //  audio.SourceDuration = TimeSpan.FromSeconds(info.Metadata.Duration);
      //}
      if (info.Metadata.Source != null)
      {
        audio.SourceMedia = info.Metadata.Source;
      }

      audio.TargetAudioContainer = audio.SourceAudioContainer;
      audio.TargetAudioCodec = audio.SourceAudioCodec;
      audio.TargetForceCopy = true;
    
      audio.TargetIsLive = true;
      audio.TranscodeId = Guid.Empty.ToString() + "_" + Client.Profile.ID;
      return audio;
    }

    private void AssignWebMetadata(MetadataContainer info)
    {
      if (info == null) return;
      List<string> profileList = new List<string>();
      if (TranscodingParameter == null)
      {
        WebMetadata = info;
      }
      else
      {
        if (IsImage)
        {
          ImageTranscoding image = (ImageTranscoding)TranscodingParameter;
          TranscodedImageMetadata metadata = MediaConverter.GetTranscodedImageMetadata(image);
          WebMetadata = new MetadataContainer();
          WebMetadata.Metadata.Mime = info.Metadata.Mime;
          WebMetadata.Metadata.ImageContainerType = metadata.TargetImageCodec;
          WebMetadata.Metadata.Size = 0;
          if(Client.EstimateTransodedSize == true)
          {
            WebMetadata.Metadata.Size = info.Metadata.Size;
          }
          WebMetadata.Image.Height = metadata.TargetMaxHeight;
          WebMetadata.Image.Orientation = metadata.TargetOrientation;
          WebMetadata.Image.PixelFormatType = metadata.TargetPixelFormat;
          WebMetadata.Image.Width = metadata.TargetMaxWidth;
        }
        else if (IsAudio)
        {
          AudioTranscoding audio = (AudioTranscoding)TranscodingParameter;
          TranscodedAudioMetadata metadata = MediaConverter.GetTranscodedAudioMetadata(audio);
          WebMetadata = new MetadataContainer();
          WebMetadata.Metadata.Mime = info.Metadata.Mime;
          WebMetadata.Metadata.AudioContainerType = metadata.TargetAudioContainer;
          WebMetadata.Metadata.Bitrate = 0;
          if(metadata.TargetAudioBitrate > 0)
          {
            WebMetadata.Metadata.Bitrate = metadata.TargetAudioBitrate;
          }
          //else if(info.Audio[0].Bitrate > 0)
          //{
          //  DlnaMetadata.Metadata.Bitrate = info.Audio[0].Bitrate;
          //}
          WebMetadata.Metadata.Duration = info.Metadata.Duration;
          WebMetadata.Metadata.Size = 0;
          if (Client.EstimateTransodedSize == true)
          {
            double audiobitrate = Convert.ToDouble(WebMetadata.Metadata.Bitrate);
            double bitrate = 0;
            if (audiobitrate > 0)
            {
              bitrate = audiobitrate * 1024; //Bitrate in bits/s
            }
            if (bitrate > 0 && WebMetadata.Metadata.Duration > 0)
            {
              WebMetadata.Metadata.Size = Convert.ToInt64((bitrate * WebMetadata.Metadata.Duration) / 8.0);
            }
          }
          AudioStream audioStream = new AudioStream();
          audioStream.Bitrate = metadata.TargetAudioBitrate;
          audioStream.Channels = metadata.TargetAudioChannels;
          audioStream.Codec = metadata.TargetAudioCodec;
          audioStream.Frequency = metadata.TargetAudioFrequency;
          WebMetadata.Audio.Add(audioStream);
        }
        else if (IsVideo)
        {
          VideoTranscoding video = (VideoTranscoding)TranscodingParameter;
          TranscodedVideoMetadata metadata = MediaConverter.GetTranscodedVideoMetadata(video);
          int selectedAudio = 0;
          for (int stream = 0; stream < info.Audio.Count; stream++)
          {
            if (video.SourceAudioStreamIndex == info.Audio[stream].StreamIndex)
            {
              selectedAudio = stream;
              break;
            }
          }

          WebMetadata = new MetadataContainer();
          WebMetadata.Metadata.Mime = info.Metadata.Mime;
          WebMetadata.Metadata.VideoContainerType = metadata.TargetVideoContainer;
          WebMetadata.Metadata.Bitrate = 0;
          if (metadata.TargetAudioBitrate > 0 && metadata.TargetVideoBitrate > 0)
          {
            WebMetadata.Metadata.Bitrate = metadata.TargetAudioBitrate + metadata.TargetVideoBitrate;
          }
          //else if (metadata.TargetAudioBitrate > 0 && info.Video.Bitrate > 0)
          //{
          //  DlnaMetadata.Metadata.Bitrate = metadata.TargetAudioBitrate + info.Video.Bitrate;
          //}
          //else if (info.Audio[selectedAudio].Bitrate > 0 && metadata.TargetVideoBitrate > 0)
          //{
          //  DlnaMetadata.Metadata.Bitrate = info.Audio[selectedAudio].Bitrate + metadata.TargetVideoBitrate;
          //}
          //else if (info.Audio[selectedAudio].Bitrate > 0 && info.Video.Bitrate > 0)
          //{
          //  DlnaMetadata.Metadata.Bitrate = info.Audio[selectedAudio].Bitrate + info.Video.Bitrate;
          //}
          WebMetadata.Metadata.Duration = info.Metadata.Duration;
          WebMetadata.Metadata.Size = 0;
          if (Client.EstimateTransodedSize == true)
          {
            double videobitrate = Convert.ToDouble(WebMetadata.Metadata.Bitrate);
            double bitrate = 0;
            if (videobitrate > 0)
            {
              bitrate = videobitrate * 1024; //Bitrate in bits/s
            }
            if (bitrate > 0 && WebMetadata.Metadata.Duration > 0)
            {
              WebMetadata.Metadata.Size = Convert.ToInt64((bitrate * WebMetadata.Metadata.Duration) / 8.0);
            }
          }

          AudioStream audioStream = new AudioStream();
          audioStream.Bitrate = metadata.TargetAudioBitrate;
          audioStream.Channels = metadata.TargetAudioChannels;
          audioStream.Codec = metadata.TargetAudioCodec;
          audioStream.Frequency = metadata.TargetAudioFrequency;
          WebMetadata.Audio.Add(audioStream);

          WebMetadata.Video.AspectRatio = metadata.TargetVideoAspectRatio;
          WebMetadata.Video.Bitrate = metadata.TargetVideoBitrate;
          WebMetadata.Video.Codec = metadata.TargetVideoCodec;
          WebMetadata.Video.Framerate = metadata.TargetVideoFrameRate;
          WebMetadata.Video.HeaderLevel = metadata.TargetLevel;
          WebMetadata.Video.ProfileType = metadata.TargetProfile;
          WebMetadata.Video.RefLevel = metadata.TargetLevel;
          WebMetadata.Video.Height = metadata.TargetVideoMaxHeight;
          WebMetadata.Video.PixelAspectRatio = metadata.TargetVideoPixelAspectRatio;
          WebMetadata.Video.PixelFormatType = metadata.TargetVideoPixelFormat;
          WebMetadata.Video.TimestampType = metadata.TargetVideoTimestamp;
          WebMetadata.Video.Width = metadata.TargetVideoMaxWidth;
        }
      }

      if (IsImage)
      {
        profileList = ProfileMime.ResolveImageProfile(WebMetadata.Metadata.ImageContainerType, WebMetadata.Image.Width, WebMetadata.Image.Height);
      }
      else if (IsAudio)
      {
        profileList = ProfileMime.ResolveAudioProfile(WebMetadata.Metadata.AudioContainerType, WebMetadata.Audio[0].Codec, WebMetadata.Audio[0].Bitrate, WebMetadata.Audio[0].Frequency, WebMetadata.Audio[0].Channels);
      }
      else if (IsVideo)
      {
        profileList = ProfileMime.ResolveVideoProfile(WebMetadata.Metadata.VideoContainerType, WebMetadata.Video.Codec, WebMetadata.Audio[0].Codec, WebMetadata.Video.ProfileType, WebMetadata.Video.HeaderLevel,
          WebMetadata.Video.Framerate, WebMetadata.Video.Width, WebMetadata.Video.Height, WebMetadata.Video.Bitrate, WebMetadata.Audio[0].Bitrate, WebMetadata.Video.TimestampType);
      }

      string mime = info.Metadata.Mime;
      ProfileMime.FindCompatibleMime(Client, profileList, ref mime);
      Mime = mime;
    }

    private MetadataContainer ParseLiveMedia(MediaItem item)
    {
      string resourcePathStr = (string)item.Aspects[ProviderResourceAspect.ASPECT_ID][ProviderResourceAspect.ATTR_RESOURCE_ACCESSOR_PATH];
      var resourcePath = ResourcePath.Deserialize(resourcePathStr);
      IResourceAccessor stra = SlimTvResourceAccessor.GetResourceAccessor(resourcePath.BasePathSegment.Path);

      MediaAnalyzer liveAnalyzer = new MediaAnalyzer();
      if (stra is ILocalFsResourceAccessor)
      {
        return liveAnalyzer.ParseVideoFile((ILocalFsResourceAccessor)stra);
      }
      else
      {
        return liveAnalyzer.ParseVideoStream((INetworkResourceAccessor)stra);
      }
    }

    public bool IsTranscoding
    {
      get
      {
        if (TranscodingParameter == null)
        {
          return false;
        }
        return MediaConverter.IsTranscodeRunning(_clientId, TranscodingParameter.TranscodeId);
      }
    }

    public bool StartTrancoding()
    {
      LastUpdated = DateTime.Now;
      return true;
    }

    public void StopTranscoding()
    {
      if (TranscodingParameter != null)
      {
        MediaConverter.StopTranscode(_clientId, TranscodingParameter.TranscodeId);
      }
    }

    public Guid StartStreaming()
    {
      Guid ret = Guid.NewGuid();
      _streams.Add(ret);
      return ret;
    }
    public void StopStreaming()
    {
      _streams.Clear();
    }
    public void StopStreaming(Guid streamId)
    {
      _streams.Remove(streamId);
    }
    public bool IsStreamActive(Guid streamId)
    {
      return _streams.Contains(streamId);
    }
    public bool IsStreaming 
    {
      get
      {
        return _streams.Count > 0;
      }
    }

    public string Mime { get; set; }
    public string SegmentDir { get; set; }
    public MetadataContainer WebMetadata { get; private set; }
    public EndPointSettings Client { get; private set; }
    public MediaItem MediaSource { get; private set; }
    public bool IsSegmented { get; private set; }
    public bool IsLive { get; private set; }
    public bool IsStreamable
    {
      get
      {
        if (IsTranscoded == false || IsTranscoding == false)
        {
          return true;
        }
        if (WebMetadata != null && WebMetadata.IsVideo == true)
        {
          if (WebMetadata.Metadata.VideoContainerType == VideoContainer.Unknown)
          {
            return false;
          }
          else if (WebMetadata.Metadata.VideoContainerType == VideoContainer.Mp4)
          {
            return false;
          }
        }
        return true;
      }
    }
    public BaseTranscoding TranscodingParameter { get; private set; }
    public BaseTranscoding SubtitleTranscodingParameter { get; private set; }
    public bool IsImage { get; private set; }
    public bool IsAudio { get; private set; }
    public bool IsVideo { get; private set; }
    public bool IsTranscoded
    {
      get
      {
        return TranscodingParameter != null;
      }
    }
    public DateTime LastUpdated { get; set; }
    internal static ILogger Logger
    {
      get { return ServiceRegistration.Get<ILogger>(); }
    }
  }
}
