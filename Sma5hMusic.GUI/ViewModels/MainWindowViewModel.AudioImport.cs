using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using Sma5hMusic.GUI.Interfaces;
using Sma5hMusic.GUI.Models;
using Sma5hMusic.GUI.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;

namespace Sma5hMusic.GUI.ViewModels
{
    public partial class MainWindowViewModel
    {
        private readonly IAudioImportService _audioImportService;
        private readonly INus3AudioBatchNormalizationService _nus3AudioBatchNormalizationService;

        public ReactiveCommand<Unit, Unit> ActionNormalizeNus3AudioFiles { get; }

        public async Task NormalizeNus3AudioFiles()
        {
            if (!_audioImportService.IsFfmpegConfigured())
            {
                await _messageDialog.ShowError(
                    "ffmpeg is not configured",
                    "Set the path to ffmpeg.exe in Global Settings before normalizing NUS3AUDIO files."
                );
                return;
            }

            var musicModsPath = _appSettings.CurrentValue.Sma5hMusic.ModPath;
            if (string.IsNullOrWhiteSpace(musicModsPath) || !Directory.Exists(musicModsPath))
            {
                await _messageDialog.ShowError(
                    "NUS3AUDIO normalization failed",
                    $"The MusicMods folder could not be found:\r\n{musicModsPath}"
                );
                return;
            }

            var files = _nus3AudioBatchNormalizationService.GetNus3AudioFiles(musicModsPath);
            if (files.Count == 0)
            {
                await _messageDialog.ShowInformation(
                    "NUS3AUDIO normalization",
                    $"No .nus3audio files were found in:\r\n{musicModsPath}"
                );
                return;
            }

            var confirm = await _messageDialog.ShowWarningConfirm(
                "Normalize NUS3AUDIO files?",
                $"This will normalize {files.Count} .nus3audio file(s) found in MusicMods and overwrite them after successful normalization.\r\n\r\nPlease make sure you have a backup before continuing."
            );

            if (!confirm)
                return;

            ScriptProgressModalWindow progressWindow = null;
            ScriptProgressModalWindowViewModel progressVm = null;
            Task progressDialogTask = null;

            using var cancellationTokenSource = new CancellationTokenSource();

            var userCancelled = false;
            var closingProgressWindowProgrammatically = false;
            Nus3AudioBatchNormalizationResult result = null;

            try
            {
                progressVm = new ScriptProgressModalWindowViewModel();
                progressVm.SetPreparing("Preparing NUS3AUDIO normalization...");

                progressWindow = new ScriptProgressModalWindow
                {
                    DataContext = progressVm,
                    Title = "NUS3AUDIO Normalization",
                    Width = 460,
                    Height = 180,
                    MinWidth = 460,
                    MinHeight = 180,
                    MaxWidth = 460,
                    MaxHeight = 180,
                    CanResize = false,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                progressWindow.Closing += (sender, args) =>
                {
                    if (closingProgressWindowProgrammatically)
                        return;

                    if (!cancellationTokenSource.IsCancellationRequested)
                    {
                        userCancelled = true;
                        cancellationTokenSource.Cancel();
                    }
                };

                progressDialogTask = progressWindow.ShowDialog(_rootDialog.Window);

                result = await _nus3AudioBatchNormalizationService.NormalizeFiles(
                    files,
                    musicModsPath,
                    (current, total, currentFile) =>
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            progressVm.SetProgress(
                                "Normalizing NUS3AUDIO files",
                                currentFile,
                                current,
                                total
                            );
                        });
                    },
                    cancellationTokenSource.Token
                );
            }
            catch (OperationCanceledException)
            {
                userCancelled = true;
            }
            catch (Exception e)
            {
                await _messageDialog.ShowError("NUS3AUDIO normalization failed", e.Message, e);
                return;
            }
            finally
            {
                if (progressWindow != null)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        closingProgressWindowProgrammatically = true;

                        if (progressWindow.IsVisible)
                            progressWindow.Close();
                    });
                }

                if (progressDialogTask != null)
                    await progressDialogTask;
            }

            if (userCancelled)
            {
                await _messageDialog.ShowInformation(
                    "NUS3AUDIO normalization cancelled",
                    result == null
                        ? "The operation was cancelled."
                        : $"The operation was cancelled.\r\nNormalized files before cancellation: {result.NormalizedFiles}/{files.Count}."
                );
                return;
            }

            if (result == null)
                return;

            if (result.FailedFiles.Count > 0)
            {
                await _messageDialog.ShowError(
                    "NUS3AUDIO normalization completed with errors",
                    $"Normalized files: {result.NormalizedFiles}/{result.TotalFiles}\r\nFailed files: {result.FailedFiles.Count}\r\n\r\nCheck the logs for details."
                );
                return;
            }

            await _messageDialog.ShowInformation(
                "NUS3AUDIO normalization complete",
                $"Normalized files: {result.NormalizedFiles}/{result.TotalFiles}."
            );
        }

        private async Task ImportAudioFiles(ModEntryViewModel managerMod, IReadOnlyCollection<string> inputFiles)
        {
            //TODO - Handle anything saving in a specific service
            _logger.LogInformation("Adding {NbrFiles} files to Mod {ModPath}", inputFiles.Count, managerMod.ModPath);
            var songsRemaining = inputFiles.Count;
            bool? previousApplyNormalization = null;
            foreach (var inputFile in inputFiles)
            {
                songsRemaining--;
                _vmToneIdCreation.LoadQueueStatus(songsRemaining);
                _vmToneIdCreation.Filename = inputFile;
                _vmToneIdCreation.LoadToneId(Path.GetFileNameWithoutExtension(inputFile));

                var requiresConversion = _audioImportService.RequiresConversion(inputFile);
                var isNus3Audio = _audioImportService.IsNus3Audio(inputFile);

                if (requiresConversion)
                {
                    try
                    {
                        var audioInfo = await _audioImportService.GetAudioInfo(inputFile);
                        _vmToneIdCreation.LoadAudioImportInfo(audioInfo.SampleRate, audioInfo.TotalSamples);
                    }
                    catch (Exception e)
                    {
                        await _messageDialog.ShowError("Audio import failed", e.Message, e);
                        continue;
                    }
                }
                else if (isNus3Audio)
                {
                    _vmToneIdCreation.LoadNus3AudioImportInfo();
                }
                else
                {
                    _vmToneIdCreation.ClearAudioImportInfo();
                }

                if (previousApplyNormalization.HasValue && _vmToneIdCreation.CanApplyNormalization)
                    _vmToneIdCreation.ApplyNormalization = previousApplyNormalization.Value;

                var modalToneIdCreation = new ToneIdCreationModalWindow() { DataContext = _vmToneIdCreation };
                var result = await modalToneIdCreation.ShowDialog<ToneIdCreationModalWindow>(_rootDialog.Window);
                if (result != null)
                {
                    string toneId = _vmToneIdCreation.ToneId;
                    var importFile = inputFile;
                    var applyNormalization = _vmToneIdCreation.ApplyNormalization;
                    if (_vmToneIdCreation.CanApplyNormalization)
                        previousApplyNormalization = applyNormalization;

                    if (applyNormalization && !_audioImportService.IsFfmpegConfigured())
                    {
                        await _messageDialog.ShowInformation(
                            "Audio normalization skipped",
                            "ffmpeg is not configured. The song will be imported without normalization."
                        );

                        applyNormalization = false;
                    }

                    if (requiresConversion)
                    {
                        try
                        {
                            importFile = await ConvertAudioFileWithProgress(
                                toneId,
                                inputFile,
                                managerMod.ModPath,
                                _vmToneIdCreation.LoopStartSample,
                                _vmToneIdCreation.LoopEndSample,
                                applyNormalization);
                        }
                        catch (Exception e)
                        {
                            await _messageDialog.ShowError("Audio import failed", e.Message, e);
                            continue;
                        }
                    }
                    else if (isNus3Audio && applyNormalization)
                    {
                        try
                        {
                            importFile = await NormalizeNus3AudioWithProgress(
                                toneId,
                                inputFile,
                                managerMod.ModPath);
                        }
                        catch (Exception e)
                        {
                            await _messageDialog.ShowError("Audio import failed", e.Message, e);
                            continue;
                        }
                    }

                    var uiBgmId = await _guiStateManager.CreateNewMusicModFromToneId(toneId, importFile, managerMod.MusicMod);
                    if (!string.IsNullOrEmpty(uiBgmId))
                    {
                        var vmBgmDbRootEntry = _viewModelManager.GetBgmDbRootViewModel(uiBgmId);
                        await EditBgmEntry(vmBgmDbRootEntry);
                    }
                }
            }
        }

        private async Task<string> ConvertAudioFileWithProgress(
            string toneId,
            string inputFile,
            string modPath,
            uint loopStartSample,
            uint loopEndSample,
            bool applyNormalization)
        {
            var progressVm = new AudioConversionProgressModalWindowViewModel();
            progressVm.SetConverting(Path.GetFileName(inputFile));

            var progressWindow = new AudioConversionProgressModalWindow
            {
                DataContext = progressVm
            };

            var closingProgrammatically = false;
            progressWindow.Closing += (sender, args) =>
            {
                if (!closingProgrammatically)
                    args.Cancel = true;
            };

            var progressDialogTask = progressWindow.ShowDialog(_rootDialog.Window);

            try
            {
                var result = await _audioImportService.ConvertToNus3Audio(
                    toneId,
                    inputFile,
                    modPath,
                    loopStartSample,
                    loopEndSample,
                    applyNormalization);

                progressVm.SetComplete();
                return result;
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    closingProgrammatically = true;

                    if (progressWindow.IsVisible)
                        progressWindow.Close();
                });

                await progressDialogTask;
            }
        }

        private async Task<string> NormalizeNus3AudioWithProgress(
            string toneId,
            string inputFile,
            string modPath)
        {
            var progressVm = new AudioConversionProgressModalWindowViewModel();
            progressVm.SetNormalizing(Path.GetFileName(inputFile));

            var progressWindow = new AudioConversionProgressModalWindow
            {
                DataContext = progressVm
            };

            var closingProgrammatically = false;
            progressWindow.Closing += (sender, args) =>
            {
                if (!closingProgrammatically)
                    args.Cancel = true;
            };

            var progressDialogTask = progressWindow.ShowDialog(_rootDialog.Window);

            try
            {
                var result = await _audioImportService.NormalizeNus3Audio(
                    toneId,
                    inputFile,
                    modPath);

                progressVm.SetComplete();
                return result;
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    closingProgrammatically = true;

                    if (progressWindow.IsVisible)
                        progressWindow.Close();
                });

                await progressDialogTask;
            }
        }
    }
}
