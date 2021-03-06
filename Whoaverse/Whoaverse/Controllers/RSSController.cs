﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.ServiceModel.Syndication;
using System.Web.Mvc;
using Whoaverse.Models;
using Whoaverse.Utils;

namespace Whoaverse.Controllers
{
    public class RssController : Controller
    {
        private readonly whoaverseEntities _db = new whoaverseEntities();
        
        // GET: rss/{subverseName}
        public ActionResult Rss(string subverseName)
        {
            var submissions = new List<Message>();

            if (subverseName != null)
            {
                // return only frontpage submissions from a given subverse
                var subverse = _db.Subverses.Find(subverseName);
                if (subverse != null)
                {
                    submissions = (from message in _db.Messages
                                   where message.Name != "deleted" && message.Subverse == subverse.name
                                   select message)
                                   .OrderByDescending(s => s.Rank)
                                   .Take(25)
                                   .ToList();
                }
            }
            else
            {
                // return site-wide frontpage submissions
                submissions = (from message in _db.Messages
                               where message.Name != "deleted"
                               join defaultsubverse in _db.Defaultsubverses on message.Subverse equals defaultsubverse.name
                               select message)
                               .OrderByDescending(s => s.Rank)
                               .Take(25)
                               .ToList();
            }

            var feed = new SyndicationFeed("WhoaVerse", "The frontpage of the Universe", new Uri("http://www.whoaverse.com"))
            {
                Language = "en-US",
                ImageUrl =
                    new Uri("http://" + System.Web.HttpContext.Current.Request.Url.Authority +
                            "/Graphics/whoaverse_padded.png")
            };

            var feedItems = new List<SyndicationItem>();

            foreach (var submission in submissions)
            {
                var commentsUrl = new Uri("http://" + System.Web.HttpContext.Current.Request.Url.Authority + "/v/" + submission.Subverse + "/comments/" + submission.Id);
                var subverseUrl = new Uri("http://" + System.Web.HttpContext.Current.Request.Url.Authority + "/v/" + submission.Subverse);

                var authorName = submission.Name;

                if (submission.Type == 1)
                {
                    // message type submission
                    if (submission.Anonymized || submission.Subverses.anonymized_mode)
                    {
                        authorName = submission.Id.ToString(CultureInfo.InvariantCulture);
                    }

                    var item = new SyndicationItem(
                    submission.Title,
                    submission.MessageContent + "</br>" + "Submitted by " + "<a href='u/" + authorName + "'>" + authorName + "</a> to <a href='" + subverseUrl + "'>" + submission.Subverse + "</a> | <a href='" + commentsUrl + "'>" + submission.Comments.Count() + " comments",
                    commentsUrl,
                    "Item ID",
                    submission.Date);
                    feedItems.Add(item);
                }
                else
                {
                    // link type submission
                    var linkUrl = new Uri(submission.MessageContent);
                    authorName = submission.Name;

                    if (submission.Anonymized || submission.Subverses.anonymized_mode)
                    {
                        authorName = submission.Id.ToString(CultureInfo.InvariantCulture);
                    }

                    // add a thumbnail if submission has one
                    if (submission.Thumbnail != null)
                    {
                        var thumbnailUrl = new Uri("http://" + System.Web.HttpContext.Current.Request.Url.Authority + "/Thumbs/" + submission.Thumbnail).ToString();
                        var item = new SyndicationItem(
                                                submission.Linkdescription,
                                                "<a xmlns='http://www.w3.org/1999/xhtml' href='" + commentsUrl + "'><img title='" + submission.Linkdescription + "' alt='" + submission.Linkdescription + "' src='" + thumbnailUrl + "' /></a>" +
                                                "</br>" +
                                                "Submitted by " + "<a href='u/" + authorName + "'>" + authorName + "</a> to <a href='" + subverseUrl + "'>" + submission.Subverse + "</a> | <a href='" + commentsUrl + "'>" + submission.Comments.Count() + " comments</a>" +
                                                " | <a href='" + linkUrl + "'>link</a>",
                                                commentsUrl,
                                                "Item ID",
                                                submission.Date);

                        feedItems.Add(item);
                    }
                    else
                    {
                        var item = new SyndicationItem(
                                                submission.Linkdescription,
                                                "Submitted by " + "<a href='u/" + authorName + "'>" + authorName + "</a> to <a href='" + subverseUrl + "'>" + submission.Subverse + "</a> | <a href='" + commentsUrl + "'>" + submission.Comments.Count() + " comments",
                                                commentsUrl,
                                                "Item ID",
                                                submission.Date);
                        feedItems.Add(item);
                    }
                }
            }

            feed.Items = feedItems;
            return new FeedResult(new Rss20FeedFormatter(feed));
        }
    }
}