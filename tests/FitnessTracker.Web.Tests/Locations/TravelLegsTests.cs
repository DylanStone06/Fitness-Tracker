using System.Net;
using FitnessTracker.Web.Data;
using FitnessTracker.Web.Tests.Infrastructure;

namespace FitnessTracker.Web.Tests.Locations;

// TRV-2: how long the journey takes from one of a person's locations to another, in that direction only (home to
// gym may differ from gym to home), with a mode label. Another user's legs are covered by T19 in
// Security/AccessControlTests.
public sealed class TravelLegsTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    [Fact]
    public async Task Add_SavesTheLeg_AndListsItByLocationName()
    {
        var (browser, home, gym) = await SignedInWithTwoLocationsAsync();

        var response = await browser.AddTravelLegAsync(home.Id, gym.Id, minutes: 25, mode: "Cycle");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var leg = await StoredData.TravelLegAsync(factory, home.Id, gym.Id);
        Assert.NotNull(leg);
        Assert.Equal((25, TravelMode.Cycle), (leg.Minutes, leg.Mode));
        Assert.Null(await StoredData.TravelLegAsync(factory, gym.Id, home.Id));
        var list = await (await browser.GetAsync("/TravelLegs")).Content.ReadAsStringAsync();
        Assert.Contains(home.Name, list);
        Assert.Contains(gym.Name, list);
    }

    [Fact]
    public async Task Add_WithTheReturnJourney_AddsBothDirections()
    {
        var (browser, home, gym) = await SignedInWithTwoLocationsAsync();

        await browser.AddTravelLegAsync(home.Id, gym.Id, minutes: 25, alsoTheReturnJourney: true);

        Assert.Equal(25, (await StoredData.TravelLegAsync(factory, home.Id, gym.Id))?.Minutes);
        Assert.Equal(25, (await StoredData.TravelLegAsync(factory, gym.Id, home.Id))?.Minutes);
    }

    // Asking for the return journey too when it already exists keeps the existing one (it may differ on purpose).
    [Fact]
    public async Task Add_WithTheReturnJourney_LeavesAnExistingReturnJourneyAlone()
    {
        var (browser, home, gym) = await SignedInWithTwoLocationsAsync();
        await browser.AddTravelLegAsync(gym.Id, home.Id, minutes: 45);

        await browser.AddTravelLegAsync(home.Id, gym.Id, minutes: 25, alsoTheReturnJourney: true);

        Assert.Equal(45, (await StoredData.TravelLegAsync(factory, gym.Id, home.Id))?.Minutes);
    }

    [Fact]
    public async Task Add_ShowsAnError_WhenFromAndToAreTheSamePlace()
    {
        var (browser, home, _) = await SignedInWithTwoLocationsAsync();
        var countBefore = await StoredData.TravelLegCountAsync(factory);

        var response = await browser.AddTravelLegAsync(home.Id, home.Id, minutes: 5);

        await AssertShowsAnErrorAsync(response);
        Assert.Equal(countBefore, await StoredData.TravelLegCountAsync(factory));
    }

    [Fact]
    public async Task Add_ShowsAnError_WhenThatJourneyAlreadyHasATime()
    {
        var (browser, home, gym) = await SignedInWithTwoLocationsAsync();
        await browser.AddTravelLegAsync(home.Id, gym.Id, minutes: 25);

        var response = await browser.AddTravelLegAsync(home.Id, gym.Id, minutes: 30);

        await AssertShowsAnErrorAsync(response);
        Assert.Equal(25, (await StoredData.TravelLegAsync(factory, home.Id, gym.Id))?.Minutes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(601)]
    public async Task Add_ShowsAnError_ForMinutesOutOfRange(int minutes)
    {
        var (browser, home, gym) = await SignedInWithTwoLocationsAsync();

        var response = await browser.AddTravelLegAsync(home.Id, gym.Id, minutes);

        await AssertShowsAnErrorAsync(response);
        Assert.Null(await StoredData.TravelLegAsync(factory, home.Id, gym.Id));
    }

    // T19 from the other side: a leg can't point at someone else's location, even if its id is known.
    [Fact]
    public async Task Add_ShowsAnError_AndSavesNothing_ForAnotherUsersLocation()
    {
        var (_, alexsHome, _) = await SignedInWithTwoLocationsAsync();
        var (sam, samsHome, _) = await SignedInWithTwoLocationsAsync();

        var response = await sam.AddTravelLegAsync(samsHome.Id, alexsHome.Id, minutes: 10);

        await AssertShowsAnErrorAsync(response);
        Assert.Null(await StoredData.TravelLegAsync(factory, samsHome.Id, alexsHome.Id));
    }

    [Fact]
    public async Task Edit_ChangesTheTimeAndMode()
    {
        var (browser, home, gym) = await SignedInWithTwoLocationsAsync();
        await browser.AddTravelLegAsync(home.Id, gym.Id, minutes: 25);
        var id = (await StoredData.TravelLegAsync(factory, home.Id, gym.Id))!.Id;

        var response = await browser.SubmitFormAsync($"/TravelLegs/Edit/{id}", $"/TravelLegs/Edit/{id}", new()
        {
            ["FromLocationId"] = home.Id.ToString(),
            ["ToLocationId"] = gym.Id.ToString(),
            ["Minutes"] = "35",
            ["Mode"] = "Bus",
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var leg = await StoredData.TravelLegAsync(factory, home.Id, gym.Id);
        Assert.Equal((35, TravelMode.Bus), (leg!.Minutes, leg.Mode));
    }

    // Requirements §9: a form left open in another tab can't silently overwrite a newer save.
    [Fact]
    public async Task Edit_FromAnOutOfDateTab_IsNotSaved_AndSaysWhy()
    {
        var (browser, home, gym) = await SignedInWithTwoLocationsAsync();
        await browser.AddTravelLegAsync(home.Id, gym.Id, minutes: 25);
        var id = (await StoredData.TravelLegAsync(factory, home.Id, gym.Id))!.Id;
        var editUrl = $"/TravelLegs/Edit/{id}";
        var oldTab = await browser.LoadFormAsync(editUrl);
        Dictionary<string, string> Form(string minutes) => new()
        {
            ["FromLocationId"] = home.Id.ToString(),
            ["ToLocationId"] = gym.Id.ToString(),
            ["Minutes"] = minutes,
            ["Mode"] = "Walk",
        };
        await browser.SubmitFormAsync(editUrl, editUrl, Form("30"));

        var response = await browser.SubmitAsync(oldTab, editUrl, Form("40"));

        Assert.Contains("changed in another tab", await response.Content.ReadAsStringAsync());
        Assert.Equal(30, (await StoredData.TravelLegAsync(factory, home.Id, gym.Id))?.Minutes);
    }

    [Fact]
    public async Task Delete_RemovesTheLeg()
    {
        var (browser, home, gym) = await SignedInWithTwoLocationsAsync();
        await browser.AddTravelLegAsync(home.Id, gym.Id, minutes: 25);
        var id = (await StoredData.TravelLegAsync(factory, home.Id, gym.Id))!.Id;

        var response = await browser.SubmitFormAsync($"/TravelLegs/Delete/{id}", $"/TravelLegs/Delete/{id}", []);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Null(await StoredData.TravelLegAsync(factory, home.Id, gym.Id));
    }

    // Deleting a location deletes the journeys to and from it.
    [Fact]
    public async Task DeletingALocation_DeletesItsTravelLegs()
    {
        var (browser, home, gym) = await SignedInWithTwoLocationsAsync();
        await browser.AddTravelLegAsync(home.Id, gym.Id, minutes: 25, alsoTheReturnJourney: true);

        await browser.SubmitFormAsync($"/Locations/Delete/{gym.Id}", $"/Locations/Delete/{gym.Id}", []);

        Assert.Null(await StoredData.TravelLegAsync(factory, home.Id, gym.Id));
        Assert.Null(await StoredData.TravelLegAsync(factory, gym.Id, home.Id));
    }

    private async Task<(TestBrowser Browser, Location Home, Location Gym)> SignedInWithTwoLocationsAsync()
    {
        var browser = await TestBrowser.OpenSignedInAsync(factory, "alex");
        var homeName = TestBrowser.NewName("Home");
        var gymName = TestBrowser.NewName("Gym");
        await browser.AddLocationAsync(homeName);
        await browser.AddLocationAsync(gymName);
        return (browser, (await StoredData.LocationNamedAsync(factory, homeName))!, (await StoredData.LocationNamedAsync(factory, gymName))!);
    }

    private static async Task AssertShowsAnErrorAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<title>Error: ", await response.Content.ReadAsStringAsync());
    }
}
