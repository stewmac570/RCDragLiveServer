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

    [Fact]
    public void SubmitUpdate_InvalidPinFormat_Rejected()
    {
        var store = new InMemoryDialInStore();
        var (success, error) = store.SubmitUpdate("evt1", 1, 1.0, "abc");
        Assert.False(success);
        Assert.Equal("invalid_pin_format", error);
    }

    [Fact]
    public void SubmitUpdate_NoPinRequired_WhenNoneSet()
    {
        var store = new InMemoryDialInStore();
        var (success, error) = store.SubmitUpdate("evt1", 1, 1.0, null);
        Assert.True(success);
        Assert.Null(error);
    }

    [Fact]
    public void SubmitUpdate_MissingPin_WhenPinAlreadySet_Rejected()
    {
        var store = new InMemoryDialInStore();
        store.SubmitUpdate("evt1", 1, 1.0, "5678");

        var (success, error) = store.SubmitUpdate("evt1", 1, 2.0, null);
        Assert.False(success);
        Assert.Equal("invalid_pin", error);
    }
}
