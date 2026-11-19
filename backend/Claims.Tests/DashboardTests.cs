using System.Net;
using System.Net.Http.Json;
using Claims.Api.Controllers;
using Claims.Api.Data;

namespace Claims.Tests;

// Own fixture = own database, so only the seed claims are counted.
public class DashboardTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Workload_matches_the_seed()
    {
        var farah = await api.LoginAs(Seed.Farah);

        var w = (await farah.GetFromJsonAsync<Workload>("/api/dashboard/workload", ApiFixture.Json))!;
        OfficerWorkload Of(Guid id) => w.Officers.Single(o => o.OfficerId == id);

        Assert.Equal(2, w.Unassigned);
        // Chen: in review 3 d + in review 20 d (motor, overdue).
        Assert.Equal(new OfficerWorkload(Seed.Chen, "Chen Officer", 2, 0, 1, 20, 0, null), Of(Seed.Chen));
        // Devi: awaiting info 10 d (property, not overdue) + settled 2 days after submit.
        Assert.Equal(new OfficerWorkload(Seed.Devi, "Devi Officer", 1, 1, 0, 10, 1, 2.0), Of(Seed.Devi));
        // Eric: answered 16 d (motor, overdue) + rejected 1 day after submit.
        Assert.Equal(new OfficerWorkload(Seed.Eric, "Eric Officer", 1, 0, 1, 16, 1, 1.0), Of(Seed.Eric));
        Assert.Equal(new OfficerWorkload(Seed.Farah, "Farah Manager", 0, 0, 0, null, 0, null), Of(Seed.Farah));
    }

    [Theory]
    [InlineData("/api/dashboard/workload")]
    [InlineData("/api/dashboard/exposure")]
    public async Task Claimant_gets_403(string url)
    {
        var aisha = await api.LoginAs(Seed.Aisha);

        Assert.Equal(HttpStatusCode.Forbidden, (await aisha.GetAsync(url)).StatusCode);
    }
}
