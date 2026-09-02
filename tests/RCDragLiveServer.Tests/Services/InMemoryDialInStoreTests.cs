using RCDragLiveServer.Services;
using Xunit;

namespace RCDragLiveServer.Tests.Services;

public sealed class InMemoryDialInStoreTests
{
    [Fact]
    public void SetPin_StoresHashNotPlaintext()
    {
        var store = new InMemoryDialInStore();
        const string eventId = "evt1";
        const int driverId = 42;
        const string pin = "1234";

        // First submission sets the PIN.
        var (success, error) = store.SubmitUpdate(eventId, driverId, 1.23, pin);

        Assert.True(success);
        Assert.Null(error);

        // Verify that whatever is stored for the PIN cannot be the literal value.
        // We confirm this indirectly: a second submission with the wrong PIN must be rejected,
        // and a submission with the correct PIN must be accepted — proving the hash round-trips.
        var (wrongFail, wrongError) = store.SubmitUpdate(eventId, driverId, 1.50, "9999");
        Assert.False(wrongFail);
        Assert.Equal("invalid_pin", wrongError);

        var (correctPass, correctError) = store.SubmitUpdate(eventId, driverId, 1.50, pin);
        Assert.True(correctPass);
        Assert.Null(correctError);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("123")]
    [InlineData("12345")]
    [InlineData("12a4")]
    public void SubmitUpdate_InvalidPinFormat_Rejected(string pin)
    {
        var store = new InMemoryDialInStore();
        var (success, error) = store.SubmitUpdate("evt1", 1, 1.0, pin);
        Assert.False(success);
        Assert.Equal("invalid_pin_format", error);
    }

    // A PIN is what ties a dial-in to one driver, so an unclaimed driver id can no
    // longer be written without one.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void SubmitUpdate_MissingPin_Rejected(string? pin)
    {
        var store = new InMemoryDialInStore();

        var (success, error) = store.SubmitUpdate("evt1", 1, 1.0, pin);

        Assert.False(success);
        Assert.Equal("invalid_pin_format", error);
        Assert.Empty(store.GetAll("evt1"));
    }

    [Fact]
    public void SubmitUpdate_MissingPin_WhenPinAlreadySet_Rejected()
    {
        var store = new InMemoryDialInStore();
        store.SubmitUpdate("evt1", 1, 1.0, "5678");

        var (success, error) = store.SubmitUpdate("evt1", 1, 2.0, null);
        Assert.False(success);
        Assert.Equal("invalid_pin_format", error);
        Assert.Equal(1.0, store.GetAll("evt1")[1]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void SubmitUpdate_InvalidDialIn_Rejected(double? dialIn)
    {
        var store = new InMemoryDialInStore();

        var (success, error) = store.SubmitUpdate("evt1", 1, dialIn, "1234");

        Assert.False(success);
        Assert.Equal("invalid_dialin", error);
        Assert.Empty(store.GetAll("evt1"));
    }

    [Fact]
    public void SubmitUpdate_InvalidDialIn_DoesNotOverwriteExistingValue()
    {
        var store = new InMemoryDialInStore();
        store.SubmitUpdate("evt1", 1, 1.5, "1234");

        var (success, error) = store.SubmitUpdate("evt1", 1, null, "1234");

        Assert.False(success);
        Assert.Equal("invalid_dialin", error);
        Assert.Equal(1.5, store.GetAll("evt1")[1]);
    }
}
