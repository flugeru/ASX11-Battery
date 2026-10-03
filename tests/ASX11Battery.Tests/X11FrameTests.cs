using ASX11Battery.Core.Providers;
using Xunit;

namespace ASX11Battery.Tests;

/// <summary>
/// The X11 report decoder, exercised against frames captured from real hardware.
/// This is the one place where a wrong byte offset would show the user a
/// confident, wrong number. The rule they all enforce is the same: when a frame
/// is not a battery frame, say nothing â€” never invent a percentage.
/// </summary>
public sealed class X11FrameTests
{
    /// <summary>
    /// Builds the frame Windows actually returns: the 64-byte report with the
    /// signature at offset 0 and the level at offset 4.
    /// </summary>
    private static byte[] Frame(byte model, int level, int length = 64)
    {
        var frame = new byte[length];
        frame[0] = 0x03;
        frame[1] = model;
        frame[2] = 0x40;
        frame[3] = 0x01;
        frame[4] = (byte)level;
        return frame;
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(20, 20)]
    [InlineData(90, 90)]
    [InlineData(100, 100)]
    public void ReadsTheLevelByteForEveryValidPercentage(int level, int expected)
    {
        Assert.True(X11Frame.TryDecode(Frame(0x55, level), out var result));
        Assert.True(result.Ok);
        Assert.Equal(expected, result.Percent);
        Assert.Equal("Attack Shark X11", result.Model);
    }

    [Fact]
    public void FindsTheSignatureAtOffsetZero()
    {
        Assert.True(X11Frame.TryDecode(Frame(0x55, 91), out var result));
        Assert.Equal(0, result.ReportIdShift);
        Assert.Equal(4, result.PercentOffset);
    }

    [Fact]
    public void FindsTheSignatureAtOffsetOneWhenWindowsPrefixesTheReportId()
    {
        // Some collections hand back the report id as byte 0, shifting the whole
        // signature right by one. The decoder must not be pinned to one layout.
        var frame = new byte[64];
        frame[0] = 0x01;          // report id
        frame[1] = 0x03;
        frame[2] = 0x55;
        frame[3] = 0x40;
        frame[4] = 0x01;
        frame[5] = 0x5C;          // 92%

        Assert.True(X11Frame.TryDecode(frame, out var result));
        Assert.True(result.Ok);
        Assert.Equal(92, result.Percent);
        Assert.Equal(1, result.ReportIdShift);
        Assert.Equal(5, result.PercentOffset);
    }

    [Theory]
    [InlineData(0x55, "Attack Shark X11")]
    [InlineData(0xBE, "Attack Shark X11 Pro")]
    [InlineData(0x07, "Attack Shark X11 SE")]
    public void RecognisesEveryKnownModelId(byte model, string expectedName)
    {
        Assert.True(X11Frame.TryDecode(Frame(model, 50), out var result));
        Assert.Equal(expectedName, result.Model);
    }

    [Fact]
    public void RejectsAnUnknownModelIdRatherThanGuessing()
    {
        // A movement byte landing where the model id goes must not become a level.
        Assert.False(X11Frame.TryDecode(Frame(0x99, 88), out _));
    }

    [Fact]
    public void IgnoresAnOrdinaryMovementFrame()
    {
        // 0x00 is the "all buttons released" movement report. It is the most common
        // frame the device sends, so misreading it would be visible immediately.
        var movement = new byte[8] { 0x00, 0x02, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00 };
        Assert.False(X11Frame.TryDecode(movement, out _));
    }

    [Fact]
    public void IgnoresAnAllZeroIdleFrame()
    {
        Assert.False(X11Frame.TryDecode(new byte[64], out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    public void DoesNotThrowOnATruncatedFrame(int length)
    {
        var frame = new byte[length];
        if (length > 4)
        {
            frame[0] = 0x03;
            frame[1] = 0x55;
            frame[2] = 0x40;
            frame[3] = 0x01;
        }

        // The contract is only that it does not throw: a frame too short to hold a
        // signature simply is not a battery frame.
        X11Frame.TryDecode(frame, out _);
    }

    [Theory]
    [InlineData(101)]
    [InlineData(150)]
    [InlineData(255)]
    public void ReportsAnOutOfRangeLevelAsRecognisedButNotUsable(int level)
    {
        // Distinguishing this from "not a battery frame" matters: a firmware that
        // renumbers the fields has to be visible in diagnostics rather than looking
        // like a device that is simply silent.
        Assert.True(X11Frame.TryDecode(Frame(0x55, level), out var result));
        Assert.False(result.Ok);
        Assert.Null(result.Percent);
        Assert.Contains("out of range", result.Note);
        Assert.Equal("Attack Shark X11", result.Model);
    }

    [Fact]
    public void NeverReportsAPercentageForAFrameItRejected()
    {
        // The single property the whole application is built around: an unreadable
        // device produces null, not a number.
        foreach (var frame in new[]
                 {
                     new byte[64],
                     Frame(0x55, 0xFF),
                     new byte[] { 0x03, 0x55, 0x40 },
                     new byte[] { 0x03, 0x00, 0x40, 0x01, 0xFF },
                 })
        {
            if (X11Frame.TryDecode(frame, out var result) && result.Ok)
                Assert.InRange(result.Percent!.Value, 0, 100);
            else
                Assert.Null(result.Percent);
        }
    }

    [Fact]
    public void TheX11ModelIdIsTheOneVerifiedOnHardware()
    {
        // 0x55 was measured on a physical X11: frame 03 55 40 01 5C read 92%
        // consistently. If the model table is ever edited, this documents why.
        Assert.True(X11Frame.Models.ContainsKey(0x55));
    }
}
