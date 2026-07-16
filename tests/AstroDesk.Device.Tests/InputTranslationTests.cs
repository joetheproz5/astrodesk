using AstroDesk.Device.Adb;
using AstroDesk.Device.Input;

namespace AstroDesk.Device.Tests;

public sealed class InputTranslationTests
{
    [Fact]
    public void Translate_LeftDownPacksMappedClientCoordinatesAndButtonFlag()
    {
        var translated = InputMessageTranslator.Translate(
            new PointerInput(
                PointerAction.LeftButtonDown,
                new MappedPoint(321, 654)));

        Assert.Equal(InputMessageTranslator.WmLeftButtonDown, translated.Message);
        Assert.Equal((nuint)1, translated.WParam);
        Assert.Equal(321, LowWord(translated.LParam));
        Assert.Equal(654, HighWord(translated.LParam));
        Assert.False(translated.UsesScreenCoordinates);
    }

    [Fact]
    public void Translate_DragMoveCarriesLeftButtonFlag()
    {
        var translated = InputMessageTranslator.Translate(
            new PointerInput(
                PointerAction.Move,
                new MappedPoint(20, 30),
                PointerButtons.Left));

        Assert.Equal(InputMessageTranslator.WmMouseMove, translated.Message);
        Assert.Equal((nuint)1, translated.WParam);
    }

    [Fact]
    public void Translate_WheelUsesSignedDeltaAndScreenCoordinates()
    {
        var translated = InputMessageTranslator.Translate(
            new PointerInput(
                PointerAction.Wheel,
                new MappedPoint(20, 30),
                WheelDelta: -120));

        Assert.Equal(InputMessageTranslator.WmMouseWheel, translated.Message);
        Assert.Equal(-120, unchecked((short)((ulong)translated.WParam >> 16)));
        Assert.True(translated.UsesScreenCoordinates);
    }

    [Fact]
    public void Translate_KeyUpSetsPreviousAndTransitionBits()
    {
        var translated = InputMessageTranslator.Translate(
            new KeyboardInput(VirtualKey.Escape, KeyAction.Up));
        var keyData = unchecked((uint)(int)translated.LParam);

        Assert.Equal(InputMessageTranslator.WmKeyUp, translated.Message);
        Assert.NotEqual(0u, keyData & (1u << 30));
        Assert.NotEqual(0u, keyData & (1u << 31));
    }

    [Theory]
    [InlineData(DeviceSpecialKey.Back, VirtualKey.Escape, InputModifiers.None, AndroidKeyCode.Back)]
    [InlineData(DeviceSpecialKey.Home, VirtualKey.H, InputModifiers.Alt, AndroidKeyCode.Home)]
    [InlineData(DeviceSpecialKey.RecentApps, VirtualKey.S, InputModifiers.Alt, AndroidKeyCode.AppSwitch)]
    [InlineData(DeviceSpecialKey.VolumeUp, VirtualKey.Up, InputModifiers.Alt, AndroidKeyCode.VolumeUp)]
    [InlineData(DeviceSpecialKey.Power, VirtualKey.P, InputModifiers.Alt, AndroidKeyCode.Power)]
    public void SpecialKeyMap_MapsScrcpyChordAndAdbFallback(
        DeviceSpecialKey key,
        VirtualKey expectedKey,
        InputModifiers expectedModifiers,
        AndroidKeyCode expectedFallback)
    {
        var chord = SpecialKeyMap.Get(key);

        Assert.Equal(expectedKey, chord.Key);
        Assert.Equal(expectedModifiers, chord.Modifiers);
        Assert.Equal(expectedFallback, chord.AdbFallbackKey);
    }

    [Fact]
    public void ForwardPointer_ConvertsWheelPointToScreenCoordinates()
    {
        var sink = new RecordingMessageSink
        {
            ScreenOffset = new MappedPoint(1000, 2000),
        };
        var forwarder = new Win32InputForwarder(sink, new FakeClipboard());

        var succeeded = forwarder.ForwardPointer(
            new nint(42),
            new PointerInput(
                PointerAction.Wheel,
                new MappedPoint(10, 20),
                WheelDelta: 120));

        Assert.True(succeeded);
        var message = Assert.Single(sink.Messages);
        Assert.Equal(1010, LowWord(message.LParam));
        Assert.Equal(2020, HighWord(message.LParam));
    }

    [Fact]
    public void PasteClipboard_SetsClipboardThenPostsScrcpyPasteChord()
    {
        var sink = new RecordingMessageSink();
        var clipboard = new FakeClipboard();
        var forwarder = new Win32InputForwarder(sink, clipboard);

        var succeeded = forwarder.PasteClipboard(new nint(42), "Orion");

        Assert.True(succeeded);
        Assert.Equal("Orion", clipboard.Text);
        Assert.Equal(4, sink.Messages.Count);
        Assert.Equal(InputMessageTranslator.WmSysKeyDown, sink.Messages[0].Message);
        Assert.Equal((nuint)VirtualKey.Alt, sink.Messages[0].WParam);
        Assert.Equal((nuint)VirtualKey.V, sink.Messages[1].WParam);
    }

    private static int LowWord(nint value) => unchecked((short)((long)value & 0xFFFF));

    private static int HighWord(nint value) => unchecked((short)(((long)value >> 16) & 0xFFFF));

    private sealed class RecordingMessageSink : IWindowMessageSink
    {
        public List<TranslatedInputMessage> Messages { get; } = [];

        public MappedPoint ScreenOffset { get; init; }

        public bool Post(nint windowHandle, TranslatedInputMessage message)
        {
            Messages.Add(message);
            return true;
        }

        public bool TryClientToScreen(
            nint windowHandle,
            MappedPoint clientPoint,
            out MappedPoint screenPoint)
        {
            screenPoint = new MappedPoint(
                clientPoint.X + ScreenOffset.X,
                clientPoint.Y + ScreenOffset.Y);
            return true;
        }

        public bool TryFocus(nint windowHandle) => true;
    }

    private sealed class FakeClipboard : IClipboardBridge
    {
        public string? Text { get; private set; }

        public bool TrySetText(string text)
        {
            Text = text;
            return true;
        }
    }
}
