using System.IO;
using System.Text.Json;

namespace ClickBoard.App.Settings;

public sealed record TransformationRecipe(string Name, string[] Steps, string? Project = null);

public static class TransformationRecipes
{
    public static readonly IReadOnlyDictionary<string, string> Actions = new Dictionary<string, string>
    {
        ["trim"] = "Recortar bordes",
        ["upper"] = "MAYÚSCULAS",
        ["lower"] = "minúsculas",
        ["remove-empty-lines"] = "Quitar líneas vacías",
        ["distinct-lines"] = "Quitar líneas duplicadas"
    };

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClickBoard",
        "recipes.json");

    public static List<TransformationRecipe> Load()
    {
        if (!File.Exists(FilePath)) return [];
        if (new FileInfo(FilePath).Length > 1_048_576)
            throw new IOException("El archivo de recetas supera el tamaño permitido.");
        var recipes = JsonSerializer.Deserialize<List<TransformationRecipe>>(File.ReadAllText(FilePath)) ?? [];
        Validate(recipes);
        return recipes;
    }

    private static void Validate(IReadOnlyList<TransformationRecipe> recipes)
    {
        var invalid = recipes.Count > 100
            || recipes.Any(recipe =>
                recipe is null
                || string.IsNullOrWhiteSpace(recipe.Name) || recipe.Name.Length > 80
                || recipe.Project?.Length > 80
                || recipe.Steps is null
                || recipe.Steps.Length is < 1 or > 20
                || recipe.Steps.Any(step => step is null || !Actions.ContainsKey(step)))
            || recipes
                .Select(recipe => (recipe.Project ?? "").ToUpperInvariant() + "\0" + recipe.Name.ToUpperInvariant())
                .Distinct()
                .Count() != recipes.Count;

        if (invalid)
            throw new InvalidDataException("Las recetas contienen nombres o pasos inválidos.");
    }

    public static void Save(IReadOnlyList<TransformationRecipe> recipes)
    {
        Validate(recipes);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(recipes));
            File.Move(temporary, FilePath, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
