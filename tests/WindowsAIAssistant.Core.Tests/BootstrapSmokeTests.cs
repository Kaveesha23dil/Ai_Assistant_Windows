using WindowsAIAssistant.Core.Common;

namespace WindowsAIAssistant.Core.Tests;

public class BootstrapSmokeTests
{
    [Fact]
    public void TestFramework_Is_Available()
    {
        Assert.True(true);
    }
}

public sealed class ResultTests
{
    [Fact]
    public void APlainSuccessSaysNothingExtra()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.SuccessMessage);
    }

    [Fact]
    public void ASuccessCanCarrySomethingToSay()
    {
        // Saving settings can succeed and still need something said, such as the change needing
        // a restart. The plain success overload has no room for that, so this one exists.
        var result = Result.Success("Saved. Restart to apply.");

        Assert.True(result.IsSuccess);
        Assert.Equal("Saved. Restart to apply.", result.SuccessMessage);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void AFailedResultCarriesTheReasonAndNoSuccessMessage()
    {
        var result = Result.Failure("The disk is full.");

        Assert.True(result.IsFailure);
        Assert.Equal("The disk is full.", result.ErrorMessage);
        Assert.Null(result.SuccessMessage);
    }
}
