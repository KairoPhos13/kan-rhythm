// Copyright (c) kan-rhythm contributors. Licensed under the MIT Licence.
// See the LICENCE file in the kan-rhythm repository root for full licence text.

using NUnit.Framework;
using Kan.Core.Score;

namespace Kan.Core.Tests
{
    /// <summary>Cover credits stored in the level.</summary>
    [TestFixture]
    public class KanCreditsTest
    {
        [Test]
        public void RoundTripKeepsPerformersAndDropsBlanks()
        {
            var credits = new KanCredits
            {
                CoverUrl = " https://www.youtube.com/watch?v=abc ",
                Note = "Thank you!",
                Performers =
                {
                    new CoverPerformer { Name = "Ana", Role = "ocarina", Links = { "instagram.com/ana", "  ", "ko-fi.com/ana" } },
                    new CoverPerformer(), // left blank in the editor
                    new CoverPerformer { Name = "Leo", Role = "guitar" },
                },
            };

            var back = KanCredits.FromJson(credits.ToJson())!;

            Assert.That(back.CoverUrl, Is.EqualTo("https://www.youtube.com/watch?v=abc"));
            Assert.That(back.Note, Is.EqualTo("Thank you!"));
            Assert.That(back.Performers, Has.Count.EqualTo(2));
            Assert.That(back.Performers[0].Name, Is.EqualTo("Ana"));
            Assert.That(back.Performers[0].Role, Is.EqualTo("ocarina"));
            Assert.That(back.Performers[0].Links, Is.EqualTo(new[] { "instagram.com/ana", "ko-fi.com/ana" }));
            Assert.That(back.Performers[1].Links, Is.Empty);
        }

        [Test]
        public void BadOrPartialFilesAreHandled()
        {
            Assert.That(KanCredits.FromJson("not json"), Is.Null);

            var partial = KanCredits.FromJson("{\"Performers\":[{\"Name\":\"Ana\"}]}")!;
            Assert.That(partial.CoverUrl, Is.Empty);
            Assert.That(partial.Performers[0].Links, Is.Empty);
            Assert.That(new KanCredits().IsEmpty, Is.True);
            Assert.That(partial.IsEmpty, Is.False);
        }

        [TestCase("youtube.com/@ana", "https://youtube.com/@ana")]
        [TestCase("https://www.instagram.com/ana/", "https://www.instagram.com/ana/")]
        [TestCase("http://ko-fi.com/ana", "http://ko-fi.com/ana")]
        [TestCase("javascript:alert(1)", null)]
        [TestCase("file:///etc/passwd", null)]
        [TestCase("ana on youtube", null)]
        [TestCase("ana", null)]
        [TestCase("", null)]
        public void OnlyWebLinksAreOpened(string typed, string? expected)
            => Assert.That(KanCredits.NormaliseLink(typed), Is.EqualTo(expected));

        [TestCase("https://www.youtube.com/@ana", "YouTube")]
        [TestCase("youtu.be/xyz", "YouTube")]
        [TestCase("m.facebook.com/ana", "Facebook")]
        [TestCase("ana.bandcamp.com", "Bandcamp")]
        [TestCase("twitter.com/ana", "X")]
        [TestCase("example.org/ana", "example.org")]
        [TestCase("notalink", "")]
        public void SitesAreNamed(string link, string site)
            => Assert.That(KanCredits.SiteName(link), Is.EqualTo(site));
    }
}
