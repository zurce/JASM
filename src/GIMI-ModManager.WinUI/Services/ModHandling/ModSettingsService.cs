using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.Core.Entities.Mods.Contract;
using GIMI_ModManager.Core.Entities.Mods.Exceptions;
using GIMI_ModManager.Core.Entities.Mods.Helpers;
using GIMI_ModManager.Core.Helpers;
using GIMI_ModManager.WinUI.Contracts.Services;
using GIMI_ModManager.WinUI.Services.Notifications;
using OneOf;
using OneOf.Types;
using Serilog;
using static GIMI_ModManager.WinUI.Helpers.HandlerServiceHelpers;
using Success = ErrorOr.Success;

namespace GIMI_ModManager.WinUI.Services.ModHandling;

public class ModSettingsService
{
    private readonly ISkinManagerService _skinManagerService;
    private readonly ILogger _logger;
    private readonly Notifications.NotificationManager _notificationManager;

    public ModSettingsService(ISkinManagerService skinManagerService,
        Notifications.NotificationManager notificationManager,
        ILogger logger)
    {
        _skinManagerService = skinManagerService;
        _notificationManager = notificationManager;
        _logger = logger.ForContext<ModSettingsService>();
    }


    public async Task<OneOf<Success, NotFound, ModNotFound, Error<Exception>>> SetCharacterSkinOverrideLegacy(Guid modId,
        string skinName)
    {
        var mod = _skinManagerService.GetModById(modId);

        if (mod is null)
            return new ModNotFound(modId);

        var modSettings = await GetSettingsAsync(modId);

        if (modSettings.TryPickT0(out var settings, out var errorResults))
        {
            var newSettings = settings.DeepCopyWithProperties(characterSkinOverride: NewValue<string?>.Set(skinName));
            try
            {
                await mod.Settings.SaveSettingsAsync(newSettings);
                return new Success();
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to save settings for mod {modName}", mod.Name);

                _notificationManager.ShowNotification(string.Format(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("Notification_SettingsSaveFailed") ?? "Failed to save settings for mod {0}", mod.Name),
                    string.Format(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("Notification_SettingsErrorMsg") ?? "An Error Occurred. Reason: {0}", e.Message),
                    TimeSpan.FromSeconds(5));

                return new Error<Exception>(e);
            }
        }

        if (errorResults.IsT0)
            return errorResults.AsT0;

        if (errorResults.IsT1)
            return errorResults.AsT1;

        return errorResults.AsT2;
    }

    public async Task<OneOf<ModSettings, NotFound, ModNotFound, Error<Exception>>> GetSettingsAsync(Guid modId,
        bool forceReload = false)
    {
        var mod = _skinManagerService.GetModById(modId);

        if (mod is null)
        {
            Debugger.Break();
            _logger.Debug("Could not find mod with id {ModId}", modId);
            return new ModNotFound(modId);
        }

        try
        {
            return await mod.Settings.ReadSettingsAsync().ConfigureAwait(false);
        }
        catch (ModSettingsNotFoundException e)
        {
            _logger.Error(e, "Could not find settings file for mod {ModName}", mod.Name);
            _notificationManager.ShowNotification(string.Format(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("Notification_SettingsFileNotFound") ?? "Could not find settings file for mod {0}", mod.Name), "",
                TimeSpan.FromSeconds(5));
            return new NotFound();
        }
        catch (Exception e)
        {
            _logger.Error(e, "Failed to read settings for mod {modName}", mod.Name);

            _notificationManager.ShowNotification(string.Format(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("Notification_SettingsReadFailed") ?? "Failed to read settings for mod {0}", mod.Name),
                string.Format(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("Notification_SettingsReadErrorMsg") ?? "An error occurred. Reason: {0}", e.Message),
                TimeSpan.FromSeconds(5));

            return new Error<Exception>(e);
        }
    }


    public async Task<Result<ModSettings>> SaveSettingsAsync(Guid modId, UpdateSettingsRequest change, CancellationToken cancellationToken = default)
    {
        return await CommandWrapperAsync(async () =>
        {
            if (!change.AnyUpdates)
            {
                var localizer = App.GetService<ILanguageLocalizer>();
                return Result<ModSettings>.Error(new SimpleNotification(localizer.GetLocalizedStringOrDefault("Notification_NoChanges") ?? "No changes were detected", localizer.GetLocalizedStringOrDefault("Notification_NoChangesMsg") ?? "Mod settings not updated"));
            }

            var mod = _skinManagerService.GetModById(modId);

            if (mod is null)
                throw new ModNotFoundException(modId);

            var oldModSettings = await mod.Settings.TryReadSettingsAsync(cancellationToken: cancellationToken);

            if (oldModSettings is null)
                throw new ModSettingsNotFoundException(mod);

            if (change.ImagePath.HasValue
                && change.ImagePath.Value.ValueToSet != null
                && change.ImagePath.Value.ValueToSet.IsFile
                && !File.Exists(change.ImagePath.Value.ValueToSet.LocalPath)
               )
            {
                change.ImagePath = null;
                var localizer2 = App.GetService<ILanguageLocalizer>();
                _notificationManager.ShowNotification(localizer2.GetLocalizedStringOrDefault("Notification_ImagePathNotFound") ?? "Image path not found",
                    localizer2.GetLocalizedStringOrDefault("Notification_ImagePathNotFoundMsg") ?? "When saving mod settings the currently set image could not be found",
                    TimeSpan.FromSeconds(5));
            }


            var newModSettings = oldModSettings.DeepCopyWithProperties(
                author: change.Author.EmptyStringToNull(),
                modUrl: change.ModUrl,
                imagePath: change.ImagePath != null && change.ImagePath.Value == ImageHandlerService.StaticPlaceholderImageUri
                    ? NewValue<Uri?>.Set(null)
                    : change.ImagePath,
                characterSkinOverride: change.CharacterSkinOverride.EmptyStringToNull(),
                customName: change.CustomName.EmptyStringToNull(),
                description: change.Description.EmptyStringToNull()
            );


            await mod.Settings.SaveSettingsAsync(newModSettings).ConfigureAwait(false);

            _logger.Information("Updated modSettings for mod {ModName} ({ModPath})", mod.GetDisplayName(), mod.FullPath);


            newModSettings = await mod.Settings.TryReadSettingsAsync(useCache: true, cancellationToken: cancellationToken);

            if (newModSettings is null)
                throw new ModSettingsNotFoundException(mod);


            var localizer3 = App.GetService<ILanguageLocalizer>();
            return Result<ModSettings>.Success(newModSettings, new SimpleNotification(
                title: localizer3.GetLocalizedStringOrDefault("Notification_ModSettingsUpdated") ?? "Mod settings updated",
                message: string.Format(localizer3.GetLocalizedStringOrDefault("Notification_ModSettingsUpdatedMsg") ?? "Mod settings have been updated for {0}", mod.GetDisplayName()),
                null
            ));
        }).ConfigureAwait(false);
    }

    public async Task<Result> SetModIniAsync(Guid modId, string modIni, bool autoDetect = false)
    {
        try
        {
            return await InternalSetModIniAsync(modId, modIni, autoDetect).ConfigureAwait(false);
        }
        catch (Exception e)
        {
#if DEBUG
            throw;
#endif

            _logger.Error(e, "Failed to set mod ini for mod {modId}", modId);
            return Result.Error(e);
        }
    }

    private async Task<Result> InternalSetModIniAsync(Guid modId, string modIni, bool autoDetect = false)
    {
        var mod = _skinManagerService.GetModById(modId);

        if (mod is null)
        {
            var localizer4 = App.GetService<ILanguageLocalizer>();
            return Result.Error(new SimpleNotification(localizer4.GetLocalizedStringOrDefault("Notification_CouldNotFindMod") ?? "Could not find mod",
                localizer4.GetLocalizedStringOrDefault("Notification_CouldNotFindModMsg") ?? "An error occured trying to set Mod ini, restarting JASM may help"));
        }

        var modSettings = await mod.Settings.TryReadSettingsAsync().ConfigureAwait(false);

        if (modSettings is null)
        {
            var localizer5 = App.GetService<ILanguageLocalizer>();
            return Result.Error(new SimpleNotification(localizer5.GetLocalizedStringOrDefault("Notification_CouldNotFindModSettings") ?? "Could not find mod settings",
                localizer5.GetLocalizedStringOrDefault("Notification_CouldNotFindModMsg") ?? "An error occured trying to set Mod ini, restarting JASM may help"));
        }

        ModSettings? newSettings;
        if (modIni.IsNullOrEmpty() && autoDetect)
        {
            newSettings =
                modSettings.DeepCopyWithProperties(mergedIniPath: NewValue<Uri?>.Set(null),
                    ignoreMergedIni: NewValue<bool>.Set(false));
            await mod.Settings.SaveSettingsAsync(newSettings).ConfigureAwait(false);
            await mod.GetModIniPathAsync().ConfigureAwait(false);
            return Result.Success();
        }


        if (modIni.Trim() == string.Empty)
        {
            newSettings =
                modSettings.DeepCopyWithProperties(mergedIniPath: null, ignoreMergedIni: NewValue<bool>.Set(true));

            await mod.Settings.SaveSettingsAsync(newSettings).ConfigureAwait(false);
            return Result.Success();
        }


        var modIniUri = Uri.TryCreate(modIni, UriKind.Absolute, out var uriResult) &&
                        (uriResult.Scheme == Uri.UriSchemeFile)
            ? uriResult
            : null;

        if (modIniUri is null)
        {
            var localizer6 = App.GetService<ILanguageLocalizer>();
            return Result.Error(new SimpleNotification(localizer6.GetLocalizedStringOrDefault("Notification_InvalidModIniPath") ?? "Invalid Mod ini path",
                localizer6.GetLocalizedStringOrDefault("Notification_InvalidModIniPathMsg") ?? "An error occured trying to parse the path to the .ini"));
        }

        if (!File.Exists(modIniUri.LocalPath))
        {
            var localizer7 = App.GetService<ILanguageLocalizer>();
            return Result.Error(new SimpleNotification(localizer7.GetLocalizedStringOrDefault("Notification_ModIniNotExist") ?? "Mod ini file does not exist",
                string.Format(localizer7.GetLocalizedStringOrDefault("Notification_ModIniNotExistMsg") ?? "Could not find file at {0}", modIniUri.LocalPath)));
        }

        if (!SkinModHelpers.IsInModFolder(mod, modIniUri))
        {
            var localizer8 = App.GetService<ILanguageLocalizer>();
            return Result.Error(new SimpleNotification(localizer8.GetLocalizedStringOrDefault("Notification_ModIniNotInModFolder") ?? "Mod ini file is not in mod folder",
                string.Format(localizer8.GetLocalizedStringOrDefault("Notification_ModIniNotInModFolderMsg") ?? "The mod ini file must be in the mod folder. Mod folder: {0}\nIni path: {1}", mod.FullPath, modIniUri.LocalPath)));
        }

        newSettings =
            modSettings.DeepCopyWithProperties(mergedIniPath: NewValue<Uri?>.Set(modIniUri),
                ignoreMergedIni: NewValue<bool>.Set(false));

        await mod.Settings.SaveSettingsAsync(newSettings).ConfigureAwait(false);
        await mod.GetModIniPathAsync().ConfigureAwait(false);
        return Result.Success();
    }


    private static string? EmptyStringToNull(string? str) => string.IsNullOrWhiteSpace(str) ? null : str;
}

public readonly struct ModNotFound
{
    public ModNotFound(Guid modId)
    {
        ModId = modId;
    }

    public Guid ModId { get; }
}

public class ModNotFoundException(Guid modId) : Exception($"Could not find mod with id {modId}");

public class UpdateSettingsRequest
{
    public bool AnyUpdates =>
        GetType()
            .GetProperties()
            .Where(p => p.CanRead && p.Name != nameof(AnyUpdates))
            .Select(p => p.GetValue(this))
            .Any(value => value is not null);

    public NewValue<string?>? Author { get; set; }

    public string? SetAuthor
    {
        set => Author = NewValue<string?>.Set(value);
    }

    public NewValue<Uri?>? ModUrl { get; set; }

    public Uri? SetModUrl
    {
        set => ModUrl = NewValue<Uri?>.Set(value);
    }

    public NewValue<Uri?>? ImagePath { get; set; }

    public Uri? SetImagePath
    {
        set => ImagePath = NewValue<Uri?>.Set(value);
    }

    public NewValue<string?>? CharacterSkinOverride { get; set; }

    public string? SetCharacterSkinOverride
    {
        set => CharacterSkinOverride = NewValue<string?>.Set(value);
    }

    public NewValue<string?>? CustomName { get; set; }

    public string? SetCustomName
    {
        set => CustomName = NewValue<string?>.Set(value);
    }

    public NewValue<string?>? Description { get; set; }

    public string? SetDescription
    {
        set => Description = NewValue<string?>.Set(value);
    }


    // TODO: Could do later, too big for this PR
    //public List<string> CreateUpdateLogEntries(ModSettings newModSettings)
    //{
    //    var changeEntries = new List<string>();

    //    if (Author is not null && newModSettings.Author != Author.Value)
    //    {

    //    }

    //    return changeEntries;

    //    string CreateLogEntry(string propertyName,string oldValue, string newValue)
    //    {
    //        return $""
    //    }
    //}
}

public record Result : IResult
{
    private Result<Success> _result = Result<Success>.Success(new Success());

    public bool IsSuccess => _result.IsSuccess;
    public bool IsError => _result.IsError;
    public Exception? Exception => _result.Exception;
    public string? ErrorMessage => _result.ErrorMessage;
    public SimpleNotification? Notification => _result.Notification;

    [MemberNotNullWhen(true, nameof(Notification))]
    public bool HasNotification => Notification is not null;

    public static Result Success() => new();

    public static Result Success(SimpleNotification notification)
    {
        var result = new Result
        {
            _result = Result<Success>.Success(new Success(), notification)
        };
        return result;
    }

    public static Result Error(Exception exception)
    {
        var result = new Result
        {
            _result = Result<Success>.Error(exception)
        };
        return result;
    }

    public static Result Error(SimpleNotification notification) => new()
    {
        _result = Result<Success>.Error(notification)
    };
}

public record Result<T> : IResult
{
    public T? Value { get; init; }

    [MemberNotNullWhen(true, nameof(Value))]
    public bool IsSuccess { get; private init; }

    public bool IsError { get; init; }

    public Exception? Exception { get; init; }

    private readonly string? _errorMessage;

    public string? ErrorMessage
    {
        get
        {
            if (_errorMessage is null && Exception is not null)
                return Exception.Message;

            return _errorMessage;
        }
        init => _errorMessage = value;
    }

    public SimpleNotification? Notification { get; init; }

    [MemberNotNullWhen(true, nameof(Notification))]
    public bool HasNotification => Notification is not null;

    public static Result<T> Success(T value, SimpleNotification notification) =>
        new()
        {
            Value = value,
            IsSuccess = true,
            Notification = notification
        };

    public static Result<T> Success(T value) =>
        new()
        {
            Value = value,
            IsSuccess = true
        };

    public static Result<T> Error(Exception exception) => new()
    {
        IsError = true,
        Exception = exception,
        Notification = new SimpleNotification(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("Notification_AnErrorOccurred") ?? "An Error Occurred", exception.Message, TimeSpan.FromSeconds(5))
    };

    public static Result<T> Error(string errorMessage) => new()
    {
        IsError = true,
        ErrorMessage = errorMessage,
        Notification = new SimpleNotification(App.GetService<ILanguageLocalizer>().GetLocalizedStringOrDefault("Notification_AnErrorOccurred") ?? "An Error Occurred", errorMessage, TimeSpan.FromSeconds(5))
    };

    public static Result<T> Error(Exception exception, SimpleNotification? notification)
    {
        return new Result<T>
        {
            IsError = true,
            Exception = exception,
            Notification = notification
        };
    }

    public static Result<T> Error(SimpleNotification notification)
    {
        return new Result<T>
        {
            IsError = true,
            Notification = notification
        };
    }
}

public interface IResult
{
    public bool IsSuccess { get; }
    public bool IsError { get; }
    public Exception? Exception { get; }
    public string? ErrorMessage { get; }
    public SimpleNotification? Notification { get; }
    public bool HasNotification { get; }
}