using System.Diagnostics.CodeAnalysis;
using GIMI_ModManager.Core.Services.CommandService.Models;

namespace GIMI_ModManager.Core.Services.CommandService;

public static class SpecialVariables
{
    public static IReadOnlyList<string> AllVariables => [TargetPath];


    public const string TargetPath = "{{TargetPath}}";


    [return: NotNullIfNotNull(nameof(input))]
    public static string? ReplaceVariables(string? input, SpecialVariablesInput? specialVariables, bool quoteValuesWithSpaces = false)
    {
        if (input is null)
            return null;

        if (specialVariables is null || !specialVariables.HasAnySpecialVariables())
            return input;

        foreach (var variable in specialVariables.GetSpecialVariables())
        {
            var value = specialVariables.GetVariable(variable);

            if (quoteValuesWithSpaces && value.Contains(' '))
            {
                input = SmartReplace(input, variable, value);
            }
            else
            {
                input = input.Replace(variable, value);
            }
        }

        return input;
    }

    private static string SmartReplace(string input, string variable, string value)
    {
        int index = 0;
        while ((index = input.IndexOf(variable, index, StringComparison.Ordinal)) != -1)
        {
            int quotesBefore = 0;
            for (int i = 0; i < index; i++)
            {
                if (input[i] == '\"') quotesBefore++;
            }

            bool isInsideQuotes = quotesBefore % 2 != 0;

            if (isInsideQuotes)
            {
                input = input.Remove(index, variable.Length).Insert(index, value);
                index += value.Length;
            }
            else
            {
                string quotedValue = $"\"{value}\"";
                input = input.Remove(index, variable.Length).Insert(index, quotedValue);
                index += quotedValue.Length;
            }
        }
        return input;
    }
}