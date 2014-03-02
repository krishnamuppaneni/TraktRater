﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TraktRater.Extensions;
using TraktRater.UI;
using TraktRater.TraktAPI;
using TraktRater.TraktAPI.DataStructures;
using TraktRater.Sites.API.IMDb;
using TraktRater.Settings;

namespace TraktRater.Sites.Common.IMDb
{
    public class Helper
    {
        static Dictionary<string, TraktShowSummary> ShowSummaries = new Dictionary<string, TraktShowSummary>();

        public static TraktMovieSync GetWatchedMoviesData(List<Dictionary<string, string>> movies)
        {
            var traktMovies = new List<TraktMovieSync.Movie>();

            traktMovies.AddRange(from movie in movies
                                 select new TraktMovieSync.Movie
                                 {
                                     IMDBID = movie[IMDbFieldMapping.cIMDbID],
                                     Title = movie[IMDbFieldMapping.cTitle],
                                     Year = int.Parse(movie[IMDbFieldMapping.cYear]).ToString()
                                 });

            var movieWatchedData = new TraktMovieSync
            {
                Username = AppSettings.TraktUsername,
                Password = AppSettings.TraktPassword,
                MovieList = traktMovies
            };

            return movieWatchedData;
        }

        public static TraktShowSync GetWatchedShowsData(IEnumerable<Dictionary<string, string>> shows)
        {
            var traktShows = new List<TraktShowSync.Show>();

            traktShows.AddRange(from show in shows
                                select new TraktShowSync.Show
                                {
                                    IMDbId = show[IMDbFieldMapping.cIMDbID],
                                    Title = show[IMDbFieldMapping.cTitle],
                                    Year = show[IMDbFieldMapping.cYear]
                                });

            var traktShowsData = new TraktShowSync
            {
                Username = AppSettings.TraktUsername,
                Password = AppSettings.TraktPassword,
                Showlist = traktShows
            };

            return traktShowsData;
        }

        /// <summary>
        /// returns a list of shows with episodes to mark as watched
        /// must send to trakt per show!
        /// </summary>
        public static List<TraktEpisodeSync> GetWatchedEpisodeData(List<TraktEpisode> episodes)
        {
            var traktEpisodesSync = new List<TraktEpisodeSync>();

            // seperate episodes list into shows
            foreach (var showId in episodes.Select(e => e.TVDbId).Distinct())
            {
                var episodesInShow = episodes.Where(e => e.TVDbId == showId);
                var episodesWatchedData = new List<TraktEpisodeSync.Episode>();

                if (episodesInShow.Count() == 0) continue;

                episodesWatchedData.AddRange(from episode in episodesInShow
                                             select new TraktEpisodeSync.Episode
                                             {
                                                 EpisodeIndex = episode.Episode.ToString(),
                                                 SeasonIndex = episode.Season.ToString()
                                             });

                if (episodesWatchedData.Count() == 0) continue;

                var episodeSyncData = new TraktEpisodeSync
                {
                    Username = AppSettings.TraktUsername,
                    Password = AppSettings.TraktPassword,
                    EpisodeList = episodesWatchedData,
                    SeriesID = showId.ToString(),
                    Title = episodesInShow.First().Title,
                    Year = episodesInShow.First().Year.ToString()
                };

                traktEpisodesSync.Add(episodeSyncData);
            }

            return traktEpisodesSync;
        }

        public static TraktRateMovies GetRateMoviesData(IEnumerable<Dictionary<string, string>> movies)
        {
            var traktMovies = new List<TraktMovie>();

            traktMovies.AddRange(from movie in movies
                                 where movie[IMDbFieldMapping.cYear] != "????" && !string.IsNullOrEmpty(movie[IMDbFieldMapping.cRating])
                                 select new TraktMovie
                                 {
                                     IMDbId = movie[IMDbFieldMapping.cIMDbID],
                                     Title = movie[IMDbFieldMapping.cTitle],
                                     Year = int.Parse(movie[IMDbFieldMapping.cYear]),
                                     Rating = int.Parse(movie[IMDbFieldMapping.cRating])
                                 });

            var movieRateData = new TraktRateMovies
            {
                Username = AppSettings.TraktUsername,
                Password = AppSettings.TraktPassword,
                Movies = traktMovies
            };

            return movieRateData;
        }

        public static TraktRateShows GetRateShowsData(IEnumerable<Dictionary<string, string>> shows)
        {
            var traktShows = new List<TraktShow>();

            traktShows.AddRange(from show in shows
                                where show[IMDbFieldMapping.cYear] != "????" && !string.IsNullOrEmpty(show[IMDbFieldMapping.cRating])
                                select new TraktShow
                                {
                                    IMDbId = show[IMDbFieldMapping.cIMDbID],
                                    Title = show[IMDbFieldMapping.cTitle],
                                    Year = int.Parse(show[IMDbFieldMapping.cYear]),
                                    Rating = int.Parse(show[IMDbFieldMapping.cRating])
                                });

            var movieRateData = new TraktRateShows
            {
                Username = AppSettings.TraktUsername,
                Password = AppSettings.TraktPassword,
                Shows = traktShows
            };

            return movieRateData;
        }

        public static TraktRateEpisodes GetRateEpisodeData(IEnumerable<Dictionary<string, string>> episodes)
        {
            var traktEpisodes = new List<TraktEpisode>();

            foreach (var episode in episodes.Where(e => !string.IsNullOrEmpty(e[IMDbFieldMapping.cRating])))
            {
                // get the show information
                string showTitle = GetShowName(episode[IMDbFieldMapping.cTitle]);
                if (string.IsNullOrEmpty(showTitle)) continue;

                // get slug of show title
                string slug = showTitle.GenerateSlug();
                if (string.IsNullOrEmpty(slug)) continue;

                TraktShowSummary showSummary = new TraktShowSummary();

                if (!ShowSummaries.TryGetValue(showTitle, out showSummary))
                {
                    // get from online
                    UIUtils.UpdateStatus(string.Format("Retrieving data for {0}", showTitle));
                    showSummary = TraktAPI.TraktAPI.GetShowSummary(slug);
                    if (showSummary == null || showSummary.Seasons == null || showSummary.Seasons.Count == 0)
                    {
                        UIUtils.UpdateStatus(string.Format("Unable to get info for {0}", showTitle), true);
                        continue;
                    }

                    // store show summary
                    ShowSummaries.Add(showTitle, showSummary);
                }

                var traktEpisode = GetTraktEpisodeRateData(episode, showSummary);
                if (traktEpisode == null) continue;

                traktEpisodes.Add(traktEpisode);
            }

            var episodeRateData = new TraktRateEpisodes
            {
                Username = AppSettings.TraktUsername,
                Password = AppSettings.TraktPassword,
                Episodes = traktEpisodes
            };

            return episodeRateData;
        }

        /// <summary>
        /// Removes the episode name and returns only the show title
        /// </summary>
        static string GetShowName(string title)
        {
            if (string.IsNullOrEmpty(title)) return null;

            // IMDb populates title field in the form "show: episode"
            // Some shows also include colons so should only remove last one.
            var parts = title.Split(':');

            if (parts.Count() == 2)
                return parts[0];

            if (parts.Count() > 2)
                return title.Replace(string.Format(": {0}", parts[parts.Count() - 1].Trim()), string.Empty);

            return title;
        }

        /// <summary>
        /// returns only the episode name part of the title
        /// </summary>
        static string GetEpisodeName(string title)
        {
            if (string.IsNullOrEmpty(title)) return null;

            // IMDb populates title field in the form "show: episode"
            // Some shows also include colons so should only remove last one.
            var parts = title.Split(':');

            if (parts.Count() >= 2)
                return parts[parts.Count() - 1].Trim();

            return null;
        }

        static TraktEpisode GetTraktEpisodeRateData(Dictionary<string, string> episode, TraktShowSummary showSummary)
        {
            if (showSummary == null || showSummary.Seasons == null || showSummary.Seasons.Count == 0)
                return null;

            string episodeTitle = GetEpisodeName(episode[IMDbFieldMapping.cTitle]);

            // find episode title in list of episodes from show summary
            if (!string.IsNullOrEmpty(episodeTitle))
            {
                TraktShowSummary.TraktSeason.TraktEpisode match = null;
                foreach (var season in showSummary.Seasons)
                {
                    if (match != null) continue;
                    match = season.Episodes.FirstOrDefault(e => string.Equals(e.Title, episodeTitle, StringComparison.InvariantCultureIgnoreCase));
                }

                if (match != null)
                {
                    return new TraktEpisode
                    {
                        Episode = match.Episode,
                        Season = match.Season,
                        TVDbId = showSummary.TVDbId,
                        Title = showSummary.Title,
                        Year = showSummary.Year,
                        Rating = int.Parse(episode[IMDbFieldMapping.cRating])
                    };
                }
            }

            // we can also lookup by airDate
            string episodeAirDate = episode[IMDbFieldMapping.cReleaseDate];

            if (!string.IsNullOrEmpty(episodeAirDate))
            {
                // get epoch date
                long dateTimeEpoch = 0;
                try
                {
                    var splitDate = episodeAirDate.Split('-');
                    // parse date and add 8hours for PST
                    DateTime dateTime = new DateTime(int.Parse(splitDate[0]), int.Parse(splitDate[1]), int.Parse(splitDate[2])).AddHours(8);
                    dateTimeEpoch = dateTime.ToEpoch();
                }
                catch
                {
                    UIUtils.UpdateStatus(string.Format("Unable to get info for {0}", episode[IMDbFieldMapping.cTitle]), true);
                    return null;
                }

                TraktShowSummary.TraktSeason.TraktEpisode match = null;
                foreach (var season in showSummary.Seasons)
                {
                    if (match != null) continue;
                    match = season.Episodes.FirstOrDefault(e => e.FirstAired == dateTimeEpoch);
                }

                if (match != null)
                {
                    return new TraktEpisode
                    {
                        Episode = match.Episode,
                        Season = match.Season,
                        TVDbId = match.TVDbId,
                        Title = showSummary.Title,
                        Year = showSummary.Year,
                        Rating = int.Parse(episode[IMDbFieldMapping.cRating])
                    };
                }
            }

            UIUtils.UpdateStatus(string.Format("Unable to get info for {0}", episode[IMDbFieldMapping.cTitle]), true);
            return null;
        }
    }
}