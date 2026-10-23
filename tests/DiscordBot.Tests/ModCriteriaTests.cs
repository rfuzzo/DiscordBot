using DiscordBot.Nexus;
using static DiscordBot.Tests.TestFactory;

namespace DiscordBot.Tests;

public class ModCriteriaTests
{
    [Fact]
    public void MatchesKeywordCaseInsensitively()
    {
        var criteria = new ModCriteria("johnny", null, null);
        Assert.True(criteria.Matches(Mod(1, "Better Johnny Silverhand", Start)));
        Assert.False(criteria.Matches(Mod(2, "Judy Romance Expanded", Start)));
    }

    [Fact]
    public void AllFiltersMustMatch()
    {
        var criteria = new ModCriteria("car", "Vehicles", "someone");
        Assert.True(criteria.Matches(Mod(1, "Flying Car", Start, "Vehicles", "Someone")));
        Assert.False(criteria.Matches(Mod(2, "Flying Car", Start, "Gameplay", "Someone")));
        Assert.False(criteria.Matches(Mod(3, "Flying Car", Start, "Vehicles", "Other")));
    }

    [Fact]
    public void BuildsReadableQuestions()
    {
        Assert.Equal(
            "Will a new Cyberpunk 2077 mod with \"Johnny\" in the name be released?",
            new ModCriteria("Johnny", null, null).ToQuestion("Cyberpunk 2077"));
        Assert.Equal(
            "Will at least 3 new Cyberpunk 2077 mods in the \"Vehicles\" category by Ash be released?",
            new ModCriteria(null, "Vehicles", "Ash", 3).ToQuestion("Cyberpunk 2077"));
    }
}
