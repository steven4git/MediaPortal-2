#region Copyright (C) 2007-2015 Team MediaPortal

/*
    Copyright (C) 2007-2015 Team MediaPortal
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
using MediaPortal.Extensions.OnlineLibraries.Libraries.TvMazeV1;
using MediaPortal.Extensions.OnlineLibraries.Libraries.TvMazeV1.Data;
using MediaPortal.Common.MediaManagement.Helpers;
using MediaPortal.Common.MediaManagement.DefaultItemAspects;

namespace MediaPortal.Extensions.OnlineLibraries.TvMaze
{
  class TvMazeWrapper : ApiWrapper<TvMazeImageCollection, string>
  {
    protected TvMazeApiV1 _tvMazeHandler;

    /// <summary>
    /// Initializes the library. Needs to be called at first.
    /// </summary>
    /// <returns></returns>
    public bool Init(string cachePath)
    {
      _tvMazeHandler = new TvMazeApiV1(cachePath);
      SetDefaultLanguage(TvMazeApiV1.DefaultLanguage);
      SetCachePath(cachePath);
      return true;
    }

    #region Search

    public override bool SearchSeriesEpisode(EpisodeInfo episodeSearch, string language, out List<EpisodeInfo> episodes)
    {
      episodes = null;
      SeriesInfo seriesSearch = null;
      if (episodeSearch.SeriesTvMazeId <= 0)
      {
        seriesSearch = episodeSearch.CloneBasicSeries();
        if (!SearchSeriesUniqueAndUpdate(seriesSearch, language))
          return false;
      }

      if (episodeSearch.SeriesTvMazeId > 0 && episodeSearch.SeasonNumber.HasValue)
      {
        TvMazeSeries seriesDetail = _tvMazeHandler.GetSeries(episodeSearch.SeriesTvMazeId, false);
        List<TvMazeEpisode> seasonEpisodes = null;
        if(seriesDetail.Embedded != null && seriesDetail.Embedded.Episodes != null)
          seasonEpisodes = seriesDetail.Embedded.Episodes.Where(s => s.SeasonNumber == episodeSearch.SeasonNumber.Value).ToList();

        foreach (TvMazeEpisode episode in seasonEpisodes)
        {
          if (episodeSearch.EpisodeNumbers.Contains(episode.EpisodeNumber) || episodeSearch.EpisodeNumbers.Count == 0)
          {
            if (episodes == null)
              episodes = new List<EpisodeInfo>();

            EpisodeInfo info = new EpisodeInfo()
            {
              TvMazeId = episode.Id,
              SeriesName = seriesSearch.SeriesName,
              SeasonNumber = episode.SeasonNumber,
              EpisodeName = new LanguageText(episode.Name, true),
            };
            info.EpisodeNumbers.Add(episode.EpisodeNumber);
            info.CopyIdsFrom(seriesSearch);
            episodes.Add(info);
          }
        }
      }

      if (episodes == null)
      {
        episodes = new List<EpisodeInfo>();
        EpisodeInfo info = new EpisodeInfo()
        {
          SeriesName = seriesSearch.SeriesName,
          SeasonNumber = episodeSearch.SeasonNumber,
          EpisodeName = episodeSearch.EpisodeName,
        };
        info.CopyIdsFrom(seriesSearch);
        info.EpisodeNumbers.AddRange(episodeSearch.EpisodeNumbers);
        episodes.Add(info);
        return true;
      }

      return episodes != null;
    }

    public override bool SearchSeries(SeriesInfo seriesSearch, string language, out List<SeriesInfo> series)
    {
      series = null;
      List<TvMazeSeries> foundSeries = _tvMazeHandler.SearchSeries(seriesSearch.SeriesName.Text);
      if (foundSeries == null) return false;
      series = foundSeries.Select(s => new SeriesInfo()
      {
        TvMazeId = s.Id,
        ImdbId = s.Externals.ImDbId,
        TvdbId = s.Externals.TvDbId ?? 0,
        TvRageId = s.Externals.TvRageId ?? 0,
        SeriesName = new LanguageText(s.Name, true),
        FirstAired = s.Premiered,
      }).ToList();

      if (series.Count == 0)
      {
        foundSeries = _tvMazeHandler.SearchSeries(seriesSearch.OriginalName);
        if (foundSeries == null) return false;
        series = foundSeries.Select(s => new SeriesInfo()
        {
          TvMazeId = s.Id,
          ImdbId = s.Externals.ImDbId,
          TvdbId = s.Externals.TvDbId ?? 0,
          TvRageId = s.Externals.TvRageId ?? 0,
          SeriesName = new LanguageText(s.Name, true),
          FirstAired = s.Premiered,
        }).ToList();
      }
      return series.Count > 0;
    }

    public override bool SearchPerson(PersonInfo personSearch, string language, out List<PersonInfo> persons)
    {
      persons = null;
      List<TvMazePerson> foundPersons = _tvMazeHandler.SearchPerson(personSearch.Name);
      if (foundPersons == null) return false;
      persons = foundPersons.Select(p => new PersonInfo()
      {
        TvMazeId = p.Id,
        Name = p.Name,
      }).ToList();
      return persons.Count > 0;
    }

    #endregion

    #region Update

    public override bool UpdateFromOnlineSeries(SeriesInfo series, string language, bool cacheOnly)
    {
      TvMazeSeries seriesDetail = null;
      if (series.TvMazeId > 0)
        seriesDetail = _tvMazeHandler.GetSeries(series.TvMazeId, cacheOnly);
      if (seriesDetail == null && !string.IsNullOrEmpty(series.ImdbId))
        seriesDetail = _tvMazeHandler.GetSeriesByImDb(series.ImdbId, cacheOnly);
      if (seriesDetail == null && series.TvdbId > 0)
        seriesDetail = _tvMazeHandler.GetSeriesByTvDb(series.TvdbId, cacheOnly);
      if (seriesDetail == null) return false;

      series.TvMazeId = seriesDetail.Id;
      series.TvdbId = seriesDetail.Externals.TvDbId ?? 0;
      series.TvRageId = seriesDetail.Externals.TvRageId ?? 0;
      series.ImdbId = seriesDetail.Externals.ImDbId;

      series.SeriesName = new LanguageText(seriesDetail.Name, true);
      series.FirstAired = seriesDetail.Premiered;
      series.Description = new LanguageText(seriesDetail.Summary, true);
      series.TotalRating = seriesDetail.Rating != null && seriesDetail.Rating.Rating.HasValue ? seriesDetail.Rating.Rating.Value : 0;
      series.RatingCount = 1;
      series.Genres = seriesDetail.Genres;
      series.Networks = ConvertToCompanies(seriesDetail.Network ?? seriesDetail.WebNetwork, CompanyAspect.COMPANY_TV_NETWORK);
      if (seriesDetail.Status.IndexOf("Ended", StringComparison.InvariantCultureIgnoreCase) >= 0)
      {
        series.IsEnded = true;
      }
      if(seriesDetail.Embedded != null)
      {
        if (seriesDetail.Embedded.Cast != null)
        {
          series.Actors = ConvertToPersons(seriesDetail.Embedded.Cast, PersonAspect.OCCUPATION_ACTOR);
          series.Characters = ConvertToCharacters(seriesDetail.Embedded.Cast);
        }
        if(seriesDetail.Embedded.Episodes != null)
        {
          TvMazeEpisode nextEpisode = seriesDetail.Embedded.Episodes.Where(e => e.AirDate > DateTime.Now).FirstOrDefault();
          if (nextEpisode != null)
          {
            series.NextEpisodeName = nextEpisode.Name;
            series.NextEpisodeAirDate = nextEpisode.AirStamp;
            series.NextEpisodeSeasonNumber = nextEpisode.SeasonNumber;
            series.NextEpisodeNumber = nextEpisode.EpisodeNumber;
          }
        }
      }

      return true;
    }

    public override bool UpdateFromOnlineSeriesSeason(SeasonInfo season, string language, bool cacheOnly)
    {
      TvMazeSeries seriesDetail = null;
      TvMazeSeason seasonDetail = null;
      if (season.SeriesMovieDbId > 0)
        seriesDetail = _tvMazeHandler.GetSeries(season.SeriesMovieDbId, cacheOnly);
      if (seriesDetail == null) return false;
      if (season.SeriesMovieDbId > 0 && season.SeasonNumber.HasValue)
      {
        List<TvMazeSeason> seasons = _tvMazeHandler.GetSeriesSeasons(season.SeriesMovieDbId, cacheOnly);
        if (seasons != null)
          seasonDetail = seasons.Where(s => s.SeasonNumber == season.SeasonNumber).FirstOrDefault();
      }
      if (seasonDetail == null) return false;

      season.TvMazeId = seasonDetail.Id;

      season.SeriesMovieDbId = seriesDetail.Id;
      season.SeriesImdbId = seriesDetail.Externals.ImDbId;
      season.SeriesTvdbId = seriesDetail.Externals.TvDbId ?? 0;
      season.SeriesTvRageId = seriesDetail.Externals.TvRageId ?? 0;

      season.SeriesName = new LanguageText(seriesDetail.Name, true);
      season.FirstAired = seasonDetail.PremiereDate;
      season.SeasonNumber = seasonDetail.SeasonNumber;

      return true;
    }

    public override bool UpdateFromOnlineSeriesEpisode(EpisodeInfo episode, string language, bool cacheOnly)
    {
      List<EpisodeInfo> episodeDetails = new List<EpisodeInfo>();
      TvMazeEpisode episodeDetail = null;
      TvMazeSeries seriesDetail = null;
      
      if (episode.SeriesTvMazeId > 0 && episode.SeasonNumber.HasValue && episode.EpisodeNumbers.Count > 0)
      {
        seriesDetail = _tvMazeHandler.GetSeries(episode.SeriesTvMazeId, cacheOnly);
        if (seriesDetail == null && !string.IsNullOrEmpty(episode.SeriesImdbId))
          seriesDetail = _tvMazeHandler.GetSeriesByImDb(episode.SeriesImdbId, cacheOnly);
        if (seriesDetail == null && episode.SeriesTvdbId > 0)
          seriesDetail = _tvMazeHandler.GetSeriesByTvDb(episode.SeriesTvdbId, cacheOnly);
        if (seriesDetail == null) return false;

        foreach (int episodeNumber in episode.EpisodeNumbers)
        {
          episodeDetail = _tvMazeHandler.GetSeriesEpisode(episode.SeriesTvMazeId, episode.SeasonNumber.Value, episodeNumber, cacheOnly);
          if (episodeDetail == null) return false;

          EpisodeInfo info = new EpisodeInfo()
          {
            TvMazeId = episodeDetail.Id,

            SeriesMovieDbId = seriesDetail.Id,
            SeriesImdbId = seriesDetail.Externals.ImDbId,
            SeriesTvdbId = seriesDetail.Externals.TvDbId ?? 0,
            SeriesTvRageId = seriesDetail.Externals.TvRageId ?? 0,
            SeriesName = new LanguageText(seriesDetail.Name, true),
            SeriesFirstAired = seriesDetail.Premiered,

            SeasonNumber = episodeDetail.SeasonNumber,
            EpisodeNumbers = new List<int>(new int[] { episodeDetail.EpisodeNumber }),
            FirstAired = episodeDetail.AirDate,
            EpisodeName = new LanguageText(episodeDetail.Name, true),
            Summary = new LanguageText(episodeDetail.Summary, true),
            Genres = seriesDetail.Genres,
          };

          if (seriesDetail.Embedded != null && seriesDetail.Embedded.Cast != null)
          {
            info.Actors = ConvertToPersons(seriesDetail.Embedded.Cast, PersonAspect.OCCUPATION_ACTOR);
            info.Characters = ConvertToCharacters(seriesDetail.Embedded.Cast);
          }

          episodeDetails.Add(info);
        }
      }
      if (episodeDetails.Count > 1)
      {
        SetMultiEpisodeDetails(episode, episodeDetails);
        return true;
      }
      else if (episodeDetails.Count > 0)
      {
        SetEpisodeDetails(episode, episodeDetails[0]);
        return true;
      }
      return false;
    }

    public override bool UpdateFromOnlineSeriesPerson(PersonInfo person, string language, bool cacheOnly)
    {
      TvMazePerson personDetail = null;
      if (person.TvMazeId > 0)
        personDetail = _tvMazeHandler.GetPerson(person.TvMazeId, cacheOnly);
      if (personDetail == null) return false;

      person.TvMazeId = personDetail.Id;
      person.Name = personDetail.Name;

      return true;
    }

    public override bool UpdateFromOnlineSeriesCharacter(CharacterInfo character, string language, bool cacheOnly)
    {
      TvMazePerson personDetail = null;
      if (character.TvMazeId > 0)
        personDetail = _tvMazeHandler.GetCharacter(character.TvMazeId, cacheOnly);
      if (personDetail == null) return false;

      character.TvMazeId = personDetail.Id;
      character.Name = personDetail.Name;

      return true;
    }

    #endregion

    #region Convert

    private List<PersonInfo> ConvertToPersons(List<TvMazeCast> cast, string occupation)
    {
      if (cast == null || cast.Count == 0)
        return new List<PersonInfo>();

      List<PersonInfo> retValue = new List<PersonInfo>();
      foreach (TvMazeCast person in cast)
        retValue.Add(new PersonInfo() { TvMazeId = person.Person.Id, Name = person.Person.Name, Occupation = occupation });
      return retValue;
    }

    private List<CharacterInfo> ConvertToCharacters(List<TvMazeCast> characters)
    {
      if (characters == null || characters.Count == 0)
        return new List<CharacterInfo>();

      List<CharacterInfo> retValue = new List<CharacterInfo>();
      foreach (TvMazeCast person in characters)
        retValue.Add(new CharacterInfo()
        {
          ActorTvMazeId = person.Person.Id,
          ActorName = person.Person.Name,
          TvMazeId = person.Character.Id,
          Name = person.Character.Name
        });
      return retValue;
    }

    private List<CompanyInfo> ConvertToCompanies(TvMazeNetwork company, string type)
    {
      if (company == null)
        return new List<CompanyInfo>();

      return new List<CompanyInfo>(
        new CompanyInfo[]
        {
          new CompanyInfo()
          {
             TvMazeId = company.Id,
             Name = company.Name,
             Type = type
          }
      });
    }

    #endregion

    #region FanArt

    public override bool GetFanArt<T>(T infoObject, string language, string scope, out FanArtImageCollection<TvMazeImageCollection> images)
    {
      images = new FanArtImageCollection<TvMazeImageCollection>();

      if (scope == FanArtScope.Series)
      {
        EpisodeInfo episode = infoObject as EpisodeInfo;
        SeasonInfo season = infoObject as SeasonInfo;
        SeriesInfo series = infoObject as SeriesInfo;
        if (series == null && season != null)
        {
          series = season.CloneBasicSeries();
        }
        if (series == null && episode != null)
        {
          series = episode.CloneBasicSeries();
        }
        if (series != null && series.TvMazeId > 0)
        {
          // Download all image information, filter later!
          TvMazeSeries seriesDetail = _tvMazeHandler.GetSeries(series.TvMazeId, false);
          if(seriesDetail != null)
          {
            images.Id = series.TvMazeId.ToString();
            images.Posters.Add(seriesDetail.Images);
            return true;
          }
        }
      }
      else if (scope == FanArtScope.Episode)
      {
        EpisodeInfo episode = infoObject as EpisodeInfo;
        if (episode != null && episode.SeriesTvMazeId > 0 && episode.SeasonNumber.HasValue && episode.EpisodeNumbers.Count > 0)
        {
          // Download all image information, filter later!
          TvMazeEpisode episodeDetail = _tvMazeHandler.GetSeriesEpisode(episode.SeriesTvMazeId, episode.SeasonNumber.Value, episode.EpisodeNumbers[0], false);
          if (episodeDetail != null)
          {
            images.Id = episode.SeriesTvMazeId.ToString();
            images.Thumbnails.Add(episodeDetail.Images);
            return true;
          }
        }
      }
      else if (scope == FanArtScope.Actor)
      {
        PersonInfo person = infoObject as PersonInfo;
        if (person != null && person.TvMazeId > 0)
        {
          // Download all image information, filter later!
          TvMazePerson personDetail = _tvMazeHandler.GetPerson(person.TvMazeId, false);
          if (personDetail != null)
          {
            images.Id = person.TvMazeId.ToString();
            images.Thumbnails.Add(personDetail.Images);
            return true;
          }
        }
      }
      else if (scope == FanArtScope.Character)
      {
        CharacterInfo character = infoObject as CharacterInfo;
        if (character != null && character.TvMazeId > 0)
        {
          // Download all image information, filter later!
          TvMazePerson personDetail = _tvMazeHandler.GetCharacter(character.TvMazeId, false);
          if (personDetail != null)
          {
            images.Id = character.TvMazeId.ToString();
            images.Thumbnails.Add(personDetail.Images);
            return true;
          }
        }
      }
      else
      {
        return true;
      }
      return false;
    }

    public override bool DownloadFanArt(string id, TvMazeImageCollection image, string scope, string type)
    {
      int ID;
      if (int.TryParse(id, out ID))
      {
        string category = string.Format(@"{0}\{1}", scope, type);
        return _tvMazeHandler.DownloadImage(ID, image, category);
      }
      return false;
    }

    public override bool DownloadSeriesSeasonFanArt(string id, int seasonNo, TvMazeImageCollection image, string scope, string type)
    {
      int ID;
      if (int.TryParse(id, out ID))
      {
        string category = string.Format(@"S{0:00} {1}\{2}", seasonNo, scope, type);
        return _tvMazeHandler.DownloadImage(ID, image, category);
      }
      return false;
    }

    public override bool DownloadSeriesEpisodeFanArt(string id, int seasonNo, int episodeNo, TvMazeImageCollection image, string scope, string type)
    {
      int ID;
      if (int.TryParse(id, out ID))
      {
        string category = string.Format(@"S{0:00}E{1:00} {2}\{3}", seasonNo, episodeNo, scope, type);
        return _tvMazeHandler.DownloadImage(ID, image, category);
      }
      return false;
    }

    #endregion
  }
}
