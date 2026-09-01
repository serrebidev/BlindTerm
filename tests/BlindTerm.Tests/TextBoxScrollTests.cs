using BlindTerm.App;

namespace BlindTerm.Tests;

public class TextBoxScrollTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("one\r\n", 0)]
    [InlineData("one\r\ntwo\r\n", 5)]
    [InlineData("one\r\ntwo\r\n\r\n", 5)]
    public void LastContentLineIsFoundWithoutReadingTheDocument(string text, int expected)
        => OnAWindowThread(() =>
        {
            using var box = new TextBox { Multiline = true, Text = text };
            _ = box.Handle;

            Assert.Equal(expected, TextBoxScroll.LastContentLineStart(box));
        });

    private static void OnAWindowThread(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }
}
