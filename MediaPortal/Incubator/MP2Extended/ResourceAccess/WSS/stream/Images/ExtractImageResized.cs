﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using HttpServer;
using HttpServer.Exceptions;
using MediaPortal.Common;
using MediaPortal.Common.Logging;
using MediaPortal.Extensions.UserServices.FanArtService.Interfaces;
using MediaPortal.Plugins.MP2Extended.Common;
using MediaPortal.Plugins.MP2Extended.ResourceAccess.WSS.Cache;
using MediaPortal.Plugins.MP2Extended.ResourceAccess.WSS.stream.Images.BaseClasses;

namespace MediaPortal.Plugins.MP2Extended.ResourceAccess.WSS.stream.Images
{
  // TODO: implement offset
  internal class ExtractImageResized : IStreamRequestMicroModuleHandler
  {
    // We just return a Thumbnail from MP
    public byte[] Process(IHttpRequest request)
    {
      Stopwatch stopWatch = new Stopwatch();
      stopWatch.Start();
      
      HttpParam httpParam = request.Param;
      string id = httpParam["itemId"].Value;
      string maxWidth = httpParam["maxWidth"].Value;
      string maxHeight = httpParam["maxHeight"].Value;

      // set borders to transparent
      string borders = "transparent";
      string artworktype = ((int)WebFileType.Content).ToString();
      string mediatype = ((int)WebMediaType.File).ToString();

      bool isSeason = false;
      string showId = string.Empty;
      string seasonId = string.Empty;

      if (id == null)
        throw new BadRequestException("ExtractImageResized: id is null");
      if (maxWidth == null)
        throw new BadRequestException("ExtractImageResized: maxWidth is null");
      if (maxHeight == null)
        throw new BadRequestException("ExtractImageResized: maxHeight is null");

      // if teh Id contains a ':' it is a season
      if (id.Contains(":"))
        isSeason = true;

      int maxWidthInt;
      if (!Int32.TryParse(maxWidth, out maxWidthInt))
      {
        throw new BadRequestException(String.Format("ExtractImageResized: Couldn't convert maxWidth to int: {0}", maxWidth));
      }

      int maxHeightInt;
      if (!Int32.TryParse(maxHeight, out maxHeightInt))
      {
        throw new BadRequestException(String.Format("ExtractImageResized: Couldn't convert maxHeight to int: {0}", maxHeight));
      }

      Guid idGuid;
      if (!Guid.TryParse(isSeason ? showId : id, out idGuid))
        throw new BadRequestException(String.Format("ExtractImageResized: Couldn't parse if '{0}' to Guid", isSeason ? showId : id));

      byte[] data;
      if (ImageCache.TryGetImageFromCache(idGuid, ImageCache.GetIdentifier(), maxWidthInt, maxHeightInt, borders, out data))
      {
        Logger.Info("GetArtworkResized: got image from cache");
        stopWatch.Stop();
        Logger.Info("GetArtworkTime: {0}", stopWatch.Elapsed);
        return data;
      }

      IList<FanArtImage> fanart = BaseGetArtwork.GetFanArtImages(artworktype, mediatype, id, showId, seasonId, isSeason);

      // get a random FanArt from the List
      Random rnd = new Random();
      int r = rnd.Next(fanart.Count);
      byte[] resizedImage = Plugins.MP2Extended.WSS.Images.ResizeImage(fanart[r].BinaryData, maxWidthInt, maxHeightInt, borders);

      // Add to cache
      if (ImageCache.AddImageToCache(resizedImage, idGuid, ImageCache.GetIdentifier(), maxWidthInt, maxHeightInt, borders))
        Logger.Info("GetArtworkResized: Added image to cache");

      stopWatch.Stop();
      Logger.Info("GetArtworkTime: {0}", stopWatch.Elapsed);
      return resizedImage;
    }

    internal static ILogger Logger
    {
      get { return ServiceRegistration.Get<ILogger>(); }
    }
  }
}