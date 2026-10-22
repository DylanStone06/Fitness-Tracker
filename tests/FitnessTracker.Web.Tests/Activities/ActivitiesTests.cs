using System.Net;
using System.Text.RegularExpressions;
using FitnessTracker.Web.Data;
using FitnessTracker.Web.Tests.Infrastructure;
using NodaTime;

namespace FitnessTracker.Web.Tests.Activities;

// CAL-2, CAL-3, FIT-6: adding, changing and deleting activities, once or repeating. Work is an ordinary activity
// (ACC-5). Another user's activities are covered by T19 in Security/AccessControlTests.
public sealed class ActivitiesTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    [Fact]
    public async Task Add_SavesEveryField()
    {
        var (browser, gym) = await SignedInWithALocationAsync();
        var title = TestBrowser.NewName("Hill reps");

        var response = await browser.AddActivityAsync(title, new()
        {
            ["Category"] = "Run",
            ["LocationId"] = gym.Id.ToString(),
            ["StartDate"] = "2026-11-03",
            ["StartTime"] = "18:30",
            ["DurationHours"] = "0",
            ["DurationMinutes"] = "50",
            ["IsFixed"] = "false",
            ["Priority"] = "High",
            ["IsDemanding"] = "true",
            ["PreparationMinutes"] = "10",
            ["ChangingMinutes"] = "15",
            ["Notes"] = "  Six hills, jog back down.  ",
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var saved = await StoredData.ActivityTitledAsync(factory, title);
        Assert.NotNull(saved);
        Assert.Equal(ActivityCategory.Run, saved.Category);
        Assert.Equal(gym.Id, saved.LocationId);
        Assert.Equal(new LocalDate(2026, 11, 3), saved.StartDate);
        Assert.Equal(new LocalTime(18, 30), saved.StartTime);
        Assert.Equal(50, saved.LengthMinutes);
        Assert.False(saved.IsFixed);
        Assert.Equal(ActivityPriority.High, saved.Priority);
        Assert.True(saved.IsDemanding);
        Assert.Equal((10, 15), (saved.PreparationMinutes, saved.ChangingMinutes));
        Assert.Equal("Six hills, jog back down.", saved.Notes);
        Assert.Null(saved.RepeatRule);
    }

    // ACC-6: buffers left blank get the category's default: the built-in one (Gym: 10 and 10 minutes) until the user
    // sets their own (Run: 7 and 12). A zero typed in stays zero.
    [Theory]
    [InlineData("Gym", "", "", 10, 10)]
    [InlineData("Run", "", "", 7, 12)]
    [InlineData("Gym", "0", "0", 0, 0)]
    [InlineData("Gym", "", "3", 10, 3)]
    public async Task Add_WithBlankBuffers_UsesTheCategorysDefault(string category, string preparation, string changing, int storedPreparation, int storedChanging)
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        await browser.SubmitFormAsync("/CategoryBuffers", "/CategoryBuffers", TestBrowser.CategoryBuffersForm(new() { ["Rows[3].PreparationMinutes"] = "7", ["Rows[3].ChangingMinutes"] = "12" }));
        var title = TestBrowser.NewName("Session");

        await browser.AddActivityAsync(title, new() { ["Category"] = category, ["PreparationMinutes"] = preparation, ["ChangingMinutes"] = changing });

        var saved = await StoredData.ActivityTitledAsync(factory, title);
        Assert.Equal((storedPreparation, storedChanging), (saved?.PreparationMinutes, saved?.ChangingMinutes));
    }

    // The form shows each category's default in the empty boxes, so the user can see what blank means.
    [Fact]
    public async Task TheForm_ShowsTheDefaultsForEachCategory()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");

        var html = await (await browser.GetAsync("/Activities/Create")).Content.ReadAsStringAsync();

        Assert.Contains("data-buffers=", html);
        Assert.Contains("&quot;Gym&quot;:[10,10]", html);
    }

    // A night shift runs past midnight: its length is elapsed time (§8), so it is simply 8 hours.
    [Fact]
    public async Task Add_ANightShift_IsSavedWithItsLength()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Night shift");

        await browser.AddActivityAsync(title, new() { ["Category"] = "Work", ["StartTime"] = "22:00", ["DurationHours"] = "8" });

        Assert.Equal(8 * 60, (await StoredData.ActivityTitledAsync(factory, title))?.LengthMinutes);
    }

    // CAL-3: weekly on chosen days, every N weeks, ending after a count. Stored as RRULE text (ADR 0001), with the
    // week start from the user's preferences (Monday by default).
    [Fact]
    public async Task Add_RepeatingWeekly_StoresTheRule()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Gym");

        await browser.AddActivityAsync(title, new()
        {
            ["Repeats"] = "Weekly",
            ["RepeatEvery"] = "2",
            ["RepeatDays[0]"] = "true",
            ["RepeatDays[2]"] = "true",
            ["Ends"] = "AfterCount",
            ["EndCount"] = "10",
        });

        Assert.Equal("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE;WKST=MO;COUNT=10", (await StoredData.ActivityTitledAsync(factory, title))?.RepeatRule);
    }

    // The week start decides which weeks "every 2 weeks" falls in, so a new rule takes the user's preference.
    [Fact]
    public async Task Add_RepeatingWeekly_UsesTheWeekStartFromPreferences()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        await SaveWeekStartAsync(browser, "Sunday");
        var title = TestBrowser.NewName("Gym");

        await browser.AddActivityAsync(title, new() { ["Repeats"] = "Weekly", ["RepeatDays[0]"] = "true" });

        Assert.Equal("FREQ=WEEKLY;INTERVAL=1;BYDAY=MO;WKST=SU", (await StoredData.ActivityTitledAsync(factory, title))?.RepeatRule);
    }

    [Fact]
    public async Task Add_RepeatingDaily_UntilADate_StoresTheRule()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Stretching");

        await browser.AddActivityAsync(title, new() { ["Repeats"] = "Daily", ["Ends"] = "OnDate", ["EndDate"] = "2026-11-30" });

        Assert.Equal("FREQ=DAILY;INTERVAL=1;UNTIL=20261130", (await StoredData.ActivityTitledAsync(factory, title))?.RepeatRule);
    }

    // The repeat fields are only checked when the activity repeats: a one-off with a leftover 0 isn't refused.
    [Fact]
    public async Task Add_AOneOff_IgnoresTheRepeatFields()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Dentist");

        var response = await browser.AddActivityAsync(title, new() { ["RepeatEvery"] = "0", ["Ends"] = "AfterCount", ["EndCount"] = "" });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null((await StoredData.ActivityTitledAsync(factory, title))?.RepeatRule);
    }

    // Each problem is shown and linked from the summary at the top (GOV.UK pattern), and nothing is saved.
    [Theory]
    [InlineData("Title", "", "Title")]
    [InlineData("Category", "Juggling", "Category")]
    [InlineData("Category", "99", "Category")]
    [InlineData("Repeats", "7", "Repeats")]
    [InlineData("StartDate", "", "StartDate")]
    [InlineData("StartDate", "2026-02-30", "StartDate")]
    [InlineData("StartTime", "25:00", "StartTime")]
    [InlineData("DurationHours", "0", "DurationHours")]
    [InlineData("DurationHours", "25", "DurationHours")]
    [InlineData("DurationHours", "26", "DurationHours")]
    [InlineData("DurationMinutes", "60", "DurationMinutes")]
    [InlineData("Priority", "Urgent", "Priority")]
    [InlineData("PreparationMinutes", "-1", "PreparationMinutes")]
    [InlineData("ChangingMinutes", "241", "ChangingMinutes")]
    [InlineData("Repeats", "Monthly", "Repeats")]
    public async Task Add_ShowsAnErrorLinkedToTheField_AndSavesNothing(string field, string value, string linkedField)
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = field == "Title" ? "" : TestBrowser.NewName("Gym");
        var form = TestBrowser.ActivityForm(title, new() { [field] = value });
        if (field == "DurationHours" && value == "0")
        {
            form["DurationMinutes"] = "0";
        }

        var response = await browser.SubmitFormAsync("/Activities/Create", "/Activities/Create", form);

        var html = await AssertShowsAnErrorAsync(response);
        Assert.Contains($"href=\"#{linkedField}\"", html);
        if (title.Length > 0)
        {
            Assert.Null(await StoredData.ActivityTitledAsync(factory, title));
        }
    }

    [Fact]
    public async Task Add_ShowsAnError_ForATitleThatIsTooLong()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var countBefore = await StoredData.ActivityCountAsync(factory);

        var response = await browser.AddActivityAsync(new string('a', Activity.TitleMaxLength + 1));

        await AssertShowsAnErrorAsync(response);
        Assert.Equal(countBefore, await StoredData.ActivityCountAsync(factory));
    }

    // Problems with the repeat: only checked when it repeats, each linked to its own field.
    [Theory]
    [InlineData("Weekly", "RepeatEvery", "0", "RepeatEvery")]
    [InlineData("Daily", "RepeatEvery", "100", "RepeatEvery")]
    [InlineData("Weekly", "Ends", "Sometime", "Ends")]
    [InlineData("Weekly", "EndDate", "", "EndDate")]
    [InlineData("Weekly", "EndCount", "0", "EndCount")]
    public async Task Add_ShowsAnError_ForARepeatProblem(string repeats, string field, string value, string linkedField)
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Gym");
        var ends = field switch { "EndDate" => "OnDate", "EndCount" => "AfterCount", _ => "Never" };

        var response = await browser.AddActivityAsync(title, new()
        {
            ["Repeats"] = repeats,
            ["RepeatDays[0]"] = "true",
            ["Ends"] = ends,
            [field] = value,
        });

        var html = await AssertShowsAnErrorAsync(response);
        Assert.Contains($"href=\"#{linkedField}\"", html);
        Assert.Null(await StoredData.ActivityTitledAsync(factory, title));
    }

    [Fact]
    public async Task Add_ShowsAnError_ForAWeeklyRepeatWithNoDays()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Gym");

        var response = await browser.AddActivityAsync(title, new() { ["Repeats"] = "Weekly" });

        var html = await AssertShowsAnErrorAsync(response);
        Assert.Contains("href=\"#RepeatDays_0_\"", html);
        Assert.Null(await StoredData.ActivityTitledAsync(factory, title));
    }

    // A repeat that ends before it first happens would be saved and never appear (scheduling review of RRULE text):
    // Mondays only from Tuesday 3 November, ending that Sunday; or an end date before the start.
    [Theory]
    [InlineData("Weekly", "2026-11-08")]
    [InlineData("Daily", "2025-11-30")]
    public async Task Add_ShowsAnError_WhenTheRepeatWouldNeverHappen(string repeats, string endDate)
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Gym");

        var response = await browser.AddActivityAsync(title, new()
        {
            ["StartDate"] = "2026-11-03",
            ["Repeats"] = repeats,
            ["RepeatDays[0]"] = "true",
            ["Ends"] = "OnDate",
            ["EndDate"] = endDate,
        });

        var html = await AssertShowsAnErrorAsync(response);
        Assert.Contains("href=\"#EndDate\"", html);
        Assert.Null(await StoredData.ActivityTitledAsync(factory, title));
    }

    // Architecture rule 5: a form can't point at someone else's location, even if its id is known.
    [Fact]
    public async Task Add_ShowsAnError_AndSavesNothing_ForAnotherUsersLocation()
    {
        var (_, alexsGym) = await SignedInWithALocationAsync();
        var sam = await TestBrowser.OpenSignedInAsync(factory, "sam");
        var title = TestBrowser.NewName("Gym");

        var response = await sam.AddActivityAsync(title, new() { ["LocationId"] = alexsGym.Id.ToString() });

        var html = await AssertShowsAnErrorAsync(response);
        Assert.Contains("href=\"#LocationId\"", html);
        Assert.Null(await StoredData.ActivityTitledAsync(factory, title));
    }

    [Fact]
    public async Task List_ShowsEachActivity_InDateOrder_WithWhenItHappens()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var later = TestBrowser.NewName("Dentist");
        var earlier = TestBrowser.NewName("Gym");
        await browser.AddActivityAsync(later, new() { ["Category"] = "Appointment", ["StartDate"] = "2026-11-20", ["StartTime"] = "09:15", ["DurationMinutes"] = "30", ["DurationHours"] = "0" });
        await browser.AddActivityAsync(earlier, new()
        {
            ["Repeats"] = "Weekly",
            ["RepeatEvery"] = "2",
            ["RepeatDays[0]"] = "true",
            ["RepeatDays[2]"] = "true",
            ["DurationMinutes"] = "30",
            ["Ends"] = "AfterCount",
            ["EndCount"] = "10",
        });

        var html = await (await browser.GetAsync("/Activities")).Content.ReadAsStringAsync();

        Assert.True(html.IndexOf(earlier, StringComparison.Ordinal) < html.IndexOf(later, StringComparison.Ordinal), "Not in date order.");
        Assert.Contains("Fri 20 Nov 2026 at 09:15 for 30 min", html);
        Assert.Contains("Every 2 weeks on Mon, Wed at 07:00 for 1 h 30 min, from Mon 2 Nov 2026, 10 times", html);
    }

    [Fact]
    public async Task Edit_ShowsTheSavedValues()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Gym");
        await browser.AddActivityAsync(title, new()
        {
            ["StartTime"] = "06:45",
            ["DurationMinutes"] = "15",
            ["Repeats"] = "Weekly",
            ["RepeatEvery"] = "2",
            ["RepeatDays[4]"] = "true",
            ["Ends"] = "OnDate",
            ["EndDate"] = "2027-03-26",
        });
        var id = (await StoredData.ActivityTitledAsync(factory, title))!.Id;

        var html = await (await browser.GetAsync($"/Activities/Edit/{id}")).Content.ReadAsStringAsync();

        Assert.Equal("06:45", InputValue(html, "StartTime"));
        Assert.Equal("1", InputValue(html, "DurationHours"));
        Assert.Equal("15", InputValue(html, "DurationMinutes"));
        Assert.Equal("2", InputValue(html, "RepeatEvery"));
        Assert.Equal("2027-03-26", InputValue(html, "EndDate"));
        Assert.True(IsChecked(html, "Repeats", "Weekly"));
        Assert.True(IsChecked(html, "Ends", "OnDate"));
        Assert.True(IsChecked(html, "RepeatDays[4]", "true"), "Friday isn't ticked.");
        Assert.False(IsChecked(html, "RepeatDays[0]", "true"), "Monday is ticked.");
    }

    // A rule keeps its own week start (scheduling-rules skill): changing the preference later doesn't move an
    // existing fortnightly activity to the other weeks when it is edited.
    [Fact]
    public async Task Edit_SavesTheChanges_AndKeepsTheRulesOwnWeekStart()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Gym");
        await browser.AddActivityAsync(title, new() { ["Repeats"] = "Weekly", ["RepeatEvery"] = "2", ["RepeatDays[0]"] = "true" });
        var id = (await StoredData.ActivityTitledAsync(factory, title))!.Id;
        await SaveWeekStartAsync(browser, "Sunday");

        var response = await browser.SubmitFormAsync($"/Activities/Edit/{id}", $"/Activities/Edit/{id}", TestBrowser.ActivityForm(title, new()
        {
            ["StartTime"] = "18:00",
            ["Repeats"] = "Weekly",
            ["RepeatEvery"] = "2",
            ["RepeatDays[0]"] = "true",
            ["RepeatDays[3]"] = "true",
        }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var saved = await StoredData.ActivityTitledAsync(factory, title);
        Assert.Equal(new LocalTime(18, 0), saved?.StartTime);
        Assert.Equal("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,TH;WKST=MO", saved?.RepeatRule);
    }

    // A daily rule has no week start of its own to keep (it is always written without one), so turning it weekly
    // takes the preference, like a new rule.
    [Fact]
    public async Task Edit_FromDailyToWeekly_UsesTheWeekStartFromPreferences()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        await SaveWeekStartAsync(browser, "Sunday");
        var title = TestBrowser.NewName("Gym");
        await browser.AddActivityAsync(title, new() { ["Repeats"] = "Daily" });
        var id = (await StoredData.ActivityTitledAsync(factory, title))!.Id;

        await browser.SubmitFormAsync($"/Activities/Edit/{id}", $"/Activities/Edit/{id}", TestBrowser.ActivityForm(title, new()
        {
            ["Repeats"] = "Weekly",
            ["RepeatEvery"] = "2",
            ["RepeatDays[0]"] = "true",
        }));

        Assert.Equal("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO;WKST=SU", (await StoredData.ActivityTitledAsync(factory, title))?.RepeatRule);
    }

    // The "never happens" check uses the week start the rule will have (scheduling review): every 2 weeks on Sunday
    // from Tuesday 6 October 2026, ending Saturday the 17th, never happens with weeks from Sunday (the first would
    // be the 18th) but does with weeks from Monday (Sunday the 11th).
    [Fact]
    public async Task Add_EveryTwoWeeks_NeverHappens_WhenWeeksStartOnSunday()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        await SaveWeekStartAsync(browser, "Sunday");
        var title = TestBrowser.NewName("Gym");

        var response = await browser.AddActivityAsync(title, new(EveryOtherSundayFrom6October) { ["Ends"] = "OnDate", ["EndDate"] = "2026-10-17" });

        var html = await AssertShowsAnErrorAsync(response);
        Assert.Contains("href=\"#EndDate\"", html);
        Assert.Null(await StoredData.ActivityTitledAsync(factory, title));
    }

    // An edit is checked with the rule's own week start (Monday, from when it was added), not today's preference.
    [Fact]
    public async Task Edit_NeverHappensCheck_UsesTheRulesOwnWeekStart()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Gym");
        await browser.AddActivityAsync(title, EveryOtherSundayFrom6October);
        var id = (await StoredData.ActivityTitledAsync(factory, title))!.Id;
        await SaveWeekStartAsync(browser, "Sunday");

        var response = await browser.SubmitFormAsync($"/Activities/Edit/{id}", $"/Activities/Edit/{id}",
            TestBrowser.ActivityForm(title, new(EveryOtherSundayFrom6October) { ["Ends"] = "OnDate", ["EndDate"] = "2026-10-17" }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("FREQ=WEEKLY;INTERVAL=2;BYDAY=SU;WKST=MO;UNTIL=20261017", (await StoredData.ActivityTitledAsync(factory, title))?.RepeatRule);
    }

    [Fact]
    public async Task Edit_CanStopItRepeating()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Gym");
        await browser.AddActivityAsync(title, new() { ["Repeats"] = "Daily" });
        var id = (await StoredData.ActivityTitledAsync(factory, title))!.Id;

        await browser.SubmitFormAsync($"/Activities/Edit/{id}", $"/Activities/Edit/{id}", TestBrowser.ActivityForm(title));

        Assert.Null((await StoredData.ActivityTitledAsync(factory, title))?.RepeatRule);
    }

    // Requirements §9: a form left open in another tab can't silently overwrite a newer save.
    [Fact]
    public async Task Edit_FromAnOutOfDateTab_IsNotSaved_AndSaysWhy()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Gym");
        await browser.AddActivityAsync(title);
        var id = (await StoredData.ActivityTitledAsync(factory, title))!.Id;
        var editUrl = $"/Activities/Edit/{id}";
        var oldTab = await browser.LoadFormAsync(editUrl);
        await browser.SubmitFormAsync(editUrl, editUrl, TestBrowser.ActivityForm(title, new() { ["StartTime"] = "08:00" }));

        var response = await browser.SubmitAsync(oldTab, editUrl, TestBrowser.ActivityForm(title, new() { ["StartTime"] = "09:00" }));

        Assert.Contains("changed in another tab", await response.Content.ReadAsStringAsync());
        Assert.Equal(new LocalTime(8, 0), (await StoredData.ActivityTitledAsync(factory, title))?.StartTime);
    }

    [Fact]
    public async Task Delete_RemovesIt()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Gym");
        await browser.AddActivityAsync(title);
        var id = (await StoredData.ActivityTitledAsync(factory, title))!.Id;

        var response = await browser.SubmitFormAsync($"/Activities/Delete/{id}", $"/Activities/Delete/{id}", []);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null(await StoredData.ActivityTitledAsync(factory, title));
    }

    // Requirements §9: a delete page opened before another tab changed the activity asks again, naming it as it is now.
    [Fact]
    public async Task Delete_FromAnOutOfDateTab_AsksAgain()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Gym");
        await browser.AddActivityAsync(title);
        var id = (await StoredData.ActivityTitledAsync(factory, title))!.Id;
        var oldTab = await browser.LoadFormAsync($"/Activities/Delete/{id}");
        var renamed = TestBrowser.NewName("Swim");
        await browser.SubmitFormAsync($"/Activities/Edit/{id}", $"/Activities/Edit/{id}", TestBrowser.ActivityForm(renamed));

        var response = await browser.SubmitAsync(oldTab, $"/Activities/Delete/{id}", []);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(renamed, await response.Content.ReadAsStringAsync());
        Assert.NotNull(await StoredData.ActivityTitledAsync(factory, renamed));
    }

    // Deleting a location keeps the activities there, with no location (not deleted with it).
    [Fact]
    public async Task DeletingALocation_KeepsItsActivities_WithNoLocation()
    {
        var (browser, gym) = await SignedInWithALocationAsync();
        var title = TestBrowser.NewName("Gym");
        await browser.AddActivityAsync(title, new() { ["LocationId"] = gym.Id.ToString() });

        await browser.SubmitFormAsync($"/Locations/Delete/{gym.Id}", $"/Locations/Delete/{gym.Id}", []);

        var saved = await StoredData.ActivityTitledAsync(factory, title);
        Assert.NotNull(saved);
        Assert.Null(saved.LocationId);
    }

    // §8, CLAUDE.md rule 2: stored as clock time plus the timezone it is in, which is the user's.
    [Fact]
    public async Task Add_StoresTheUsersTimezone()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var before = TestBrowser.NewName("Gym");
        await browser.AddActivityAsync(before);
        await SavePreferencesAsync(browser, new() { ["TimeZoneId"] = "America/New_York" });
        var after = TestBrowser.NewName("Gym");

        await browser.AddActivityAsync(after, new() { ["StartDate"] = "2026-11-03" });

        Assert.Equal("America/New_York", (await StoredData.ActivityTitledAsync(factory, after))?.TimeZoneId);
    }

    // T6, §8: changing the timezone moves activities to the new one, keeping their clock times ("gym at 7am
    // wherever I am"). Another user's activities are left alone.
    [Fact]
    public async Task ChangingTheTimezone_KeepsEachActivitysClockTime_InTheNewTimezone()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Gym");
        await browser.AddActivityAsync(title, new() { ["Repeats"] = "Daily" });
        var sam = await TestBrowser.OpenSignedInAsync(factory, "sam");
        var samsTitle = TestBrowser.NewName("Run");
        await sam.AddActivityAsync(samsTitle);

        await SavePreferencesAsync(browser, new() { ["TimeZoneId"] = "America/New_York" });

        var saved = await StoredData.ActivityTitledAsync(factory, title);
        Assert.Equal(("America/New_York", new LocalTime(7, 0)), (saved?.TimeZoneId, saved?.StartTime));
        Assert.Equal("Europe/London", (await StoredData.ActivityTitledAsync(factory, samsTitle))?.TimeZoneId);
        var html = await (await browser.GetAsync("/Activities")).Content.ReadAsStringAsync();
        Assert.Contains("Every day at 07:00 for 1 h", html);
    }

    // Q8, CLAUDE.md rule 3: moving activities to a new timezone goes through the check too, and what the move breaks is
    // shown after saving. A daily 01:30 swim at a place open 18:00-02:00 is fine in Tokyo (no clock changes); in London
    // it would be there after closing on Sunday 28 March 2027, when 01:30 becomes 02:30. A clash that was already there
    // before the move (and kept with "Save anyway") isn't reported again.
    [Fact]
    public async Task ChangingTheTimezone_ShowsWhatTheMoveBreaks_AndOnlyThat()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        await SavePreferencesAsync(browser, new() { ["TimeZoneId"] = "Asia/Tokyo" });
        var poolName = TestBrowser.NewName("Late pool");
        var hours = new Dictionary<string, string> { ["IsHome"] = "true", ["HasOpeningHours"] = "true" };
        string[] week = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];
        for (var i = 0; i < week.Length; i++)
        {
            hours[$"OpeningDays[{i}].Day"] = week[i];
            hours[$"OpeningDays[{i}].Closed"] = "false";
            hours[$"OpeningDays[{i}].Opens"] = "18:00";
            hours[$"OpeningDays[{i}].Closes"] = "02:00";
        }

        await browser.AddLocationAsync(poolName, hours);
        var pool = (await StoredData.LocationNamedAsync(factory, poolName))!;
        var swim = TestBrowser.NewName("Late swim");
        await browser.AddActivityAsync(swim, new()
        {
            ["LocationId"] = pool.Id.ToString(),
            ["StartDate"] = "2026-10-20",
            ["StartTime"] = "01:30",
            ["DurationHours"] = "0",
            ["DurationMinutes"] = "20",
            ["Repeats"] = "Daily",
        });
        Assert.NotNull(await StoredData.ActivityTitledAsync(factory, swim));
        var meeting = TestBrowser.ActivityForm("Team meeting", new() { ["StartDate"] = "2026-11-02", ["StartTime"] = "09:00" });
        await browser.SubmitFormAsync("/Activities/Create", "/Activities/Create", meeting);
        var call = TestBrowser.ActivityForm("Call", new() { ["StartDate"] = "2026-11-02", ["StartTime"] = "09:30" });
        var shown = TestBrowser.FormIn(await (await browser.SubmitFormAsync("/Activities/Create", "/Activities/Create", call)).Content.ReadAsStringAsync());
        await browser.SubmitAsync(shown, "/Activities/Create", new(call) { ["ConfirmClashes"] = "true" });

        await SavePreferencesAsync(browser, new() { ["TimeZoneId"] = "Europe/London" });

        var page = await (await browser.GetAsync("/Preferences")).Content.ReadAsStringAsync();
        Assert.Contains($"{swim}:", page);
        Assert.Contains("28 Mar 2027", page);
        Assert.DoesNotContain("overlaps Team meeting", page);
    }

    // §8, T5: 01:30 doesn't exist in London on Sunday 28 March 2027 (the clocks go forward at 01:00), so the activity
    // really starts at 02:30, and the list says so rather than showing a time it won't happen at.
    [Fact]
    public async Task List_AOneOffInTheHourTheClocksSkip_SaysWhenItReallyStarts()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Swim");

        await browser.AddActivityAsync(title, new() { ["StartDate"] = "2027-03-28", ["StartTime"] = "01:30" });

        var html = await (await browser.GetAsync("/Activities")).Content.ReadAsStringAsync();
        Assert.Contains("Sun 28 Mar 2027 at 02:30 for 1 h", html);
    }

    // Dates far outside any plan (a mistyped year such as 9999) are refused rather than worked out.
    [Theory]
    [InlineData("StartDate", "9999-12-31")]
    [InlineData("StartDate", "1899-12-31")]
    [InlineData("EndDate", "9999-12-31")]
    public async Task Add_ShowsAnError_ForADateOutsideTheYearsTheAppPlans(string field, string date)
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var title = TestBrowser.NewName("Gym");

        var response = await browser.AddActivityAsync(title, new()
        {
            ["Repeats"] = "Daily",
            ["Ends"] = "OnDate",
            ["EndDate"] = "2027-01-31",
            [field] = date,
        });

        var html = await AssertShowsAnErrorAsync(response);
        Assert.Contains($"href=\"#{field}\"", html);
        Assert.Null(await StoredData.ActivityTitledAsync(factory, title));
    }

    private static Dictionary<string, string> EveryOtherSundayFrom6October { get; } = new()
    {
        ["StartDate"] = "2026-10-06",
        ["Repeats"] = "Weekly",
        ["RepeatEvery"] = "2",
        ["RepeatDays[6]"] = "true",
    };

    // A gym with travel times both ways from home, so an activity there passes the check on save (ActivityChecksTests).
    private async Task<(TestBrowser Browser, Location Gym)> SignedInWithALocationAsync()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var homeName = TestBrowser.NewName("Home");
        var name = TestBrowser.NewName("Gym");
        await browser.AddLocationAsync(homeName, new() { ["IsHome"] = "true" });
        await browser.AddLocationAsync(name);
        var home = (await StoredData.LocationNamedAsync(factory, homeName))!;
        var gym = (await StoredData.LocationNamedAsync(factory, name))!;
        await browser.AddTravelLegAsync(home.Id, gym.Id, minutes: 25, alsoTheReturnJourney: true);
        return (browser, gym);
    }

    private static Task SaveWeekStartAsync(TestBrowser browser, string day) =>
        SavePreferencesAsync(browser, new() { ["WeekStart"] = day });

    private static async Task SavePreferencesAsync(TestBrowser browser, Dictionary<string, string> changes) =>
        Assert.Equal(HttpStatusCode.Redirect, (await browser.SavePreferencesAsync(changes)).StatusCode);

    private static async Task<string> AssertShowsAnErrorAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("<title>Error: ", html);
        return html;
    }

    private static string? InputValue(string html, string name)
    {
        var input = Regex.Match(html, $"""<input[^>]*\sname="{Regex.Escape(name)}"[^>]*>""");
        var value = Regex.Match(input.Value, """\svalue="([^"]*)" """.TrimEnd());
        return value.Success ? WebUtility.HtmlDecode(value.Groups[1].Value) : null;
    }

    // Whether the radio button or checkbox with this name and value is ticked (hidden inputs are skipped).
    private static bool IsChecked(string html, string name, string value) =>
        Regex.Matches(html, $"""<input[^>]*\sname="{Regex.Escape(name)}"[^>]*>""")
            .Any(input => !input.Value.Contains("type=\"hidden\"", StringComparison.Ordinal)
                && input.Value.Contains($"value=\"{value}\"", StringComparison.Ordinal)
                && input.Value.Contains("checked=\"checked\"", StringComparison.Ordinal));
}
