using System.Diagnostics;
using System.Reflection;
using System.Windows.Forms;

namespace PS2_DATA_File_Extractor.Tests;

public sealed class ReleaseMetadataTests
{
    [Fact]
    public void EditorAssemblyUsesStableOnePointZeroVersion()
    {
        Assembly editor = typeof(Form1).Assembly;

        Assert.Equal(new Version(1, 0, 0, 0), editor.GetName().Version);
        AssemblyInformationalVersionAttribute informational = Assert.Single(
            editor.GetCustomAttributes<AssemblyInformationalVersionAttribute>());
        Assert.Equal("1.0.0", informational.InformationalVersion);

        FileVersionInfo file = FileVersionInfo.GetVersionInfo(editor.Location);
        Assert.Equal("1.0.0.0", file.FileVersion);
        Assert.Equal("1.0.0", file.ProductVersion);
    }

    [Fact]
    public void MainWindowShowsOnePointZeroTitle()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                using Form1 form = new();
                form.Show();
                Application.DoEvents();
                Assert.Equal("Backyard Baseball PS2 Editor v1.0", form.Text);
                form.Close();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Version title UI test did not finish.");
        Assert.Null(failure);
    }
}
