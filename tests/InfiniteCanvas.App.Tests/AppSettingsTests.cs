using System.IO;

namespace InfiniteCanvas.App.Tests;

public class AppSettingsTests
{
    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), "GreenBoardTests", $"reglages-{Guid.NewGuid():N}.json");

    [Fact]
    public void Absent_DonneLesValeursParDefaut()
    {
        var s = AppSettings.Load(TempFile());
        Assert.Equal(new AppSettings(), s);
        Assert.Equal(3, s.PenThickness);
        Assert.True(s.PenSmoothing);
        Assert.True(s.ConfirmClear);
        Assert.Equal(0, s.AutoSaveMinutes);
    }

    [Fact]
    public void AllerRetour_ConserveTousLesReglages()
    {
        string path = TempFile();
        var saved = new AppSettings
        {
            PenThickness = 6.5, ShapeThickness = 2, PenSmoothing = false, PenPressure = false,
            DefaultBackground = BoardBackground.Seyes, WheelZoomStep = 1.24, ConfirmClear = false, AutoSaveMinutes = 5
        };
        Assert.True(saved.Save(path));

        Assert.Equal(saved, AppSettings.Load(path));
        Assert.Contains("\"Seyes\"", File.ReadAllText(path)); // énumération lisible dans le JSON
    }

    [Fact]
    public void ValeursHorsBornes_SontRamenees()
    {
        string path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path,
            """{ "PenThickness": 500, "ShapeThickness": -3, "WheelZoomStep": 9, "AutoSaveMinutes": 7, "DefaultBackground": 99 }""");

        var s = AppSettings.Load(path);
        Assert.Equal(AppSettings.MaxThickness, s.PenThickness);
        Assert.Equal(AppSettings.MinThickness, s.ShapeThickness);
        Assert.Equal(AppSettings.MaxZoomStep, s.WheelZoomStep);
        Assert.Equal(0, s.AutoSaveMinutes);
        Assert.Equal(BoardBackground.Blanc, s.DefaultBackground);
    }

    [Fact]
    public void FichierCorrompu_DonneLesValeursParDefaut()
    {
        string path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ ceci n'est pas du JSON");

        Assert.Equal(new AppSettings(), AppSettings.Load(path));
    }

    [Fact]
    public void ReglagesPartiels_CompletesParLesValeursParDefaut()
    {
        string path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{ "PenThickness": 8 }""");

        var s = AppSettings.Load(path);
        Assert.Equal(8, s.PenThickness);
        Assert.Equal(new AppSettings() with { PenThickness = 8 }, s);
    }

    [Fact]
    public void AppInfo_LitLaVersionDuProjet()
    {
        Assert.Equal("1.0.0", AppInfo.Version);
        Assert.Equal("Alfa Computers Apps", AppInfo.Publisher);
    }
}
