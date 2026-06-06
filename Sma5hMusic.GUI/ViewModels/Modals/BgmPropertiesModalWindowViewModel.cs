using AutoMapper;
using Avalonia.Controls;
using Avalonia.Threading;
using DynamicData;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using ReactiveUI.Validation.Extensions;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Helpers;
using Sma5hMusic.GUI.Helpers;
using Sma5hMusic.GUI.Interfaces;
using Sma5hMusic.GUI.Models;
using Sma5hMusic.GUI.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Sma5hMusic.GUI.ViewModels
{
    public class BgmPropertiesModalWindowViewModel : ModalBaseViewModel<BgmEntryViewModel>
    {
        private readonly IOptionsMonitor<ApplicationSettings> _config;
        private readonly IMapper _mapper;
        private readonly ILogger _logger;
        private readonly IFileDialog _fileDialog;
        private readonly IGUIStateManager _guiStateManager;
        private readonly IViewModelManager _viewModelManager;
        private readonly IAudioImportService _audioImportService;
        private readonly IMessageDialog _messageDialog;
        private readonly IServiceProvider _serviceProvider;
        private readonly List<GameTitleEntryViewModel> _recentGameTitles;
        private readonly List<ComboItem> _recordTypes;
        private readonly List<ComboItem> _specialCategories;
        private readonly ReadOnlyObservableCollection<SeriesEntryViewModel> _series;
        private readonly ReadOnlyObservableCollection<GameTitleEntryViewModel> _games;
        private readonly ReadOnlyObservableCollection<string> _assignedInfoIds;
        private readonly Subject<Window> _whenNewRequestToAddGameEntry;
        private bool _isUpdatingSpecialRule = false;
        private string _originalGameTitleId;
        private string _originalFilename;

        public IEnumerable<GameTitleEntryViewModel> RecentGameTitles { get { return _recentGameTitles; } }
        [Reactive]
        public bool DisplayRecents { get; set; }
        [Reactive]
        public GameTitleEntryViewModel SelectedRecentAction { get; set; }

        public IObservable<Window> WhenNewRequestToAddGameEntry { get { return _whenNewRequestToAddGameEntry; } }
        public GamePropertiesModalWindowViewModel VMGamePropertiesModal { get; set; }

        public BgmDbRootEntryViewModel DbRootViewModel { get; private set; }
        public BgmStreamSetEntryViewModel StreamSetViewModel { get; private set; }
        public BgmAssignedInfoEntryViewModel AssignedInfoViewModel { get; private set; }
        [Reactive]
        public BgmStreamPropertyEntryViewModel StreamPropertyViewModel { get; private set; }
        public BgmPropertyEntryViewModel BgmPropertyViewModel { get; private set; }

        public MSBTFieldViewModel MSBTTitleEditor { get; set; }
        public MSBTFieldViewModel MSBTAuthorEditor { get; set; }
        public MSBTFieldViewModel MSBTCopyrightEditor { get; set; }
        public IEnumerable<ComboItem> RecordTypes { get { return _recordTypes; } }
        public IEnumerable<ComboItem> SpecialCategories { get { return _specialCategories; } }
        [Reactive]
        public ComboItem SelectedRecordType { get; set; }
        [Reactive]
        public GameTitleEntryViewModel SelectedGameTitleViewModel { get; set; }
        [Reactive]
        public ComboItem SelectedSpecialCategory { get; set; }
        [Reactive]
        public bool IsSpecialCategoryPinch { get; set; }
        [Reactive]
        public bool IsInSoundTest { get; set; }

        public bool IsModSong { get; set; }

        public ReadOnlyObservableCollection<SeriesEntryViewModel> Series { get { return _series; } }
        public ReadOnlyObservableCollection<GameTitleEntryViewModel> Games { get { return _games; } }
        public ReadOnlyObservableCollection<string> AssignedInfoIds { get { return _assignedInfoIds; } }

        public ReactiveCommand<Window, Unit> ActionNewGame { get; }
        public ReactiveCommand<BgmPropertyEntryViewModel, Unit> ActionChangeFile { get; }
        public ReactiveCommand<BgmPropertyEntryViewModel, Unit> ActionCalculateLoopCues { get; }
        public ReactiveCommand<Window, Unit> ActionPreviewLoops { get; }
        public ReactiveCommand<Window, Unit> ActionNormalizeSong { get; }
        public ReactiveCommand<Window, Unit> ActionClosing { get; }
        public ReactiveCommand<Unit, Unit> ActionSetVolumeToAverage { get; }
        public ReactiveCommand<Unit, Unit> ActionSetVolumeToMedian { get; }

        public BgmPropertiesModalWindowViewModel(IOptionsMonitor<ApplicationSettings> config, ILogger<BgmPropertiesModalWindowViewModel> logger, IFileDialog fileDialog,
            IMapper mapper, IGUIStateManager guiStateManager, IViewModelManager viewModelManager, IAudioImportService audioImportService,
            IMessageDialog messageDialog, IServiceProvider serviceProvider)
        {
            _config = config;
            _logger = logger;
            _mapper = mapper;
            _guiStateManager = guiStateManager;
            _viewModelManager = viewModelManager;
            _audioImportService = audioImportService;
            _messageDialog = messageDialog;
            _serviceProvider = serviceProvider;
            _fileDialog = fileDialog;
            _recordTypes = GetRecordTypes();
            _specialCategories = GetSpecialCategories();
            _whenNewRequestToAddGameEntry = new Subject<Window>();
            _recentGameTitles = new List<GameTitleEntryViewModel>();

            //Bind observables
            viewModelManager.ObservableSeries.Connect()
               .ObserveOn(RxApp.MainThreadScheduler)
               .Bind(out _series)
               .DisposeMany()
               .Subscribe();
            viewModelManager.ObservableGameTitles.Connect()
               .ObserveOn(RxApp.MainThreadScheduler)
               .Bind(out _games)
               .DisposeMany()
               .Subscribe();
            viewModelManager.ObservableAssignedInfoEntries.Connect()
               .Transform(p => p.InfoId)
               .ObserveOn(RxApp.MainThreadScheduler)
               .Bind(out _assignedInfoIds)
               .DisposeMany()
               .Subscribe();

            //Set up MSBT Fields
            var defaultLocale = _config.CurrentValue.Sma5hMusicGUI.DefaultGUILocale;
            var defaultLocaleItem = new ComboItem(defaultLocale, Constants.GetLocaleDisplayName(defaultLocale));
            MSBTTitleEditor = new MSBTFieldViewModel()
            {
                SelectedLocale = defaultLocaleItem
            };
            MSBTAuthorEditor = new MSBTFieldViewModel()
            {
                SelectedLocale = defaultLocaleItem
            };
            MSBTCopyrightEditor = new MSBTFieldViewModel()
            {
                SelectedLocale = defaultLocaleItem,
                AcceptsReturn = true
            };

            //Set up subscriber on special category
            this.WhenAnyValue(p => p.SelectedSpecialCategory).Subscribe(o => SetSpecialCategoryRules(o?.Id));
            this.WhenAnyValue(p => p.SelectedItem.StreamSetViewModel.SpecialCategory).Subscribe(o => SetSpecialCategoryRules(o));
            this.WhenAnyValue(p => p.SelectedGameTitleViewModel).Subscribe((o) => SetGameTitleId(o));
            this.WhenAnyValue(p => p.SelectedRecentAction).Subscribe(o => HandleRecentAction(o));

            //Validation
            this.ValidationRule(p => p.SelectedGameTitleViewModel,
                p => p != null && !string.IsNullOrWhiteSpace(p.UiGameTitleId),
                "Please select a game.");
            this.ValidationRule(p => p.StreamPropertyViewModel.StartPoint0,
                p => ValidateStreamPropertyTime(p), "This value must be of the format '00:00:00.000'");
            this.ValidationRule(p => p.StreamPropertyViewModel.StartPoint1,
                p => ValidateStreamPropertyTime(p), "This value must be of the format '00:00:00.000'");
            this.ValidationRule(p => p.StreamPropertyViewModel.StartPoint2,
                p => ValidateStreamPropertyTime(p), "This value must be of the format '00:00:00.000'");
            this.ValidationRule(p => p.StreamPropertyViewModel.StartPoint3,
                p => ValidateStreamPropertyTime(p), "This value must be of the format '00:00:00.000'");
            this.ValidationRule(p => p.StreamPropertyViewModel.StartPoint4,
                p => ValidateStreamPropertyTime(p), "This value must be of the format '00:00:00.000'");
            this.ValidationRule(p => p.StreamPropertyViewModel.EndPoint,
                p => ValidateStreamPropertyTime(p), "This value must be of the format '00:00:00.000'");
            this.ValidationRule(p => p.StreamPropertyViewModel.StartPointSuddenDeath,
                p => ValidateStreamPropertyTime(p), "This value must be of the format '00:00:00.000'");
            this.ValidationRule(p => p.StreamPropertyViewModel.StartPointTransition,
                p => ValidateStreamPropertyTime(p), "This value must be of the format '00:00:00.000'");

            ActionNewGame = ReactiveCommand.Create<Window>(AddNewGame);
            ActionChangeFile = ReactiveCommand.CreateFromTask<BgmPropertyEntryViewModel>(ChangeFile);
            ActionCalculateLoopCues = ReactiveCommand.CreateFromTask<BgmPropertyEntryViewModel>(CalculateAudioCues);
            ActionPreviewLoops = ReactiveCommand.CreateFromTask<Window>(PreviewLoops);
            ActionNormalizeSong = ReactiveCommand.CreateFromTask<Window>(NormalizeSong);
            ActionClosing = ReactiveCommand.Create<Window>(ClosingWindow);
            ActionSetVolumeToAverage = ReactiveCommand.Create(SetVolumeToAverage);
            ActionSetVolumeToMedian = ReactiveCommand.Create(SetVolumeToMedian);
        }

        private void SetVolumeToAverage()
        {
            var volumes = GetCurrentMetadataSongVolumes();
            if (volumes.Count == 0)
                return;

            BgmPropertyViewModel.AudioVolume = (float)Math.Round(volumes.Average(), 2);
        }

        private void SetVolumeToMedian()
        {
            var volumes = GetCurrentMetadataSongVolumes().OrderBy(p => p).ToList();
            if (volumes.Count == 0)
                return;

            var middle = volumes.Count / 2;
            var median = volumes.Count % 2 == 1
                ? volumes[middle]
                : (volumes[middle - 1] + volumes[middle]) / 2.0f;

            BgmPropertyViewModel.AudioVolume = (float)Math.Round(median, 2);
        }

        private List<float> GetCurrentMetadataSongVolumes()
        {
            return _viewModelManager.GetBgmPropertyEntriesViewModels()
                .Where(p => p.MusicMod?.Id == BgmPropertyViewModel.MusicMod?.Id)
                .Select(p => p.AudioVolume)
                .ToList();
        }

        private bool ValidateStreamPropertyTime(string value)
        {
            return string.IsNullOrEmpty(value) || Regex.IsMatch(value, @"^\d{2}:\d{2}:\d{2}.\d{3}$", RegexOptions.Compiled);
        }

        private void HandleRecentAction(GameTitleEntryViewModel o)
        {
            if (o == null)
                return;

            SelectedGameTitleViewModel = _games.FirstOrDefault(p => p.UiGameTitleId == o.UiGameTitleId);

            Dispatcher.UIThread.InvokeAsync(() =>
            {
                SelectedRecentAction = null;
            }, DispatcherPriority.Background);
        }

        private void AddRecentGameTitle(GameTitleEntryViewModel gameTitle)
        {
            if (gameTitle == null || string.IsNullOrEmpty(gameTitle.UiGameTitleId))
                return;

            _recentGameTitles.RemoveAll(p => p.UiGameTitleId == gameTitle.UiGameTitleId);

            if (_recentGameTitles.Count > 9)
                _recentGameTitles.RemoveAt(_recentGameTitles.Count - 1);

            _recentGameTitles.Insert(0, gameTitle);
            DisplayRecents = _recentGameTitles.Count > 0;
            this.RaisePropertyChanged(nameof(RecentGameTitles));
        }

        private void AddNewGame(Window window)
        {
            _logger.LogDebug("Clicked Add New Game");
            _whenNewRequestToAddGameEntry.OnNext(window);
        }

        private async Task ChangeFile(BgmPropertyEntryViewModel bgmPropertyEntryViewModel)
        {
            _logger.LogDebug("Clicked Change File");
            var filename = await _fileDialog.OpenFileDialogAudioSingle();
            if (!string.IsNullOrEmpty(filename))
            {
                var oldFile = BgmPropertyViewModel.Filename;
                BgmPropertyViewModel.Filename = filename;
                if (await CalculateAudioCues(bgmPropertyEntryViewModel))
                {
                    await BgmPropertyViewModel.MusicPlayer?.ChangeFilename(filename);
                }
                else
                {
                    BgmPropertyViewModel.Filename = oldFile;
                }
            }
        }

        private async Task PreviewLoops(Window parentWindow)
        {
            if (BgmPropertyViewModel == null)
                return;

            string previewFilename = null;

            try
            {
                _logger.LogDebug("Clicked Preview Loops");

                if (string.IsNullOrWhiteSpace(BgmPropertyViewModel.Filename) || !File.Exists(BgmPropertyViewModel.Filename))
                {
                    await _messageDialog.ShowError("Preview Loops", "The song file could not be found.");
                    return;
                }

                previewFilename = _audioImportService.IsNus3Audio(BgmPropertyViewModel.Filename)
                    ? await _audioImportService.ExtractNus3AudioToWav(BgmPropertyViewModel.Filename)
                    : BgmPropertyViewModel.Filename;

                var audioInfo = await _audioImportService.GetAudioInfo(previewFilename);
                var vmToneIdCreation = ActivatorUtilities.CreateInstance<ToneIdCreationModalWindowModel>(_serviceProvider);
                vmToneIdCreation.LoadQueueStatus(0);
                vmToneIdCreation.LoadLoopPreviewOnlyInfo(
                    previewFilename,
                    audioInfo.SampleRate,
                    audioInfo.TotalSamples,
                    BgmPropertyViewModel.LoopStartSample,
                    BgmPropertyViewModel.LoopEndSample
                );

                await vmToneIdCreation.PrepareForOpen();

                var modalToneIdCreation = new ToneIdCreationModalWindow() { DataContext = vmToneIdCreation };
                var result = await modalToneIdCreation.ShowDialog<ToneIdCreationModalWindow>(parentWindow);

                if (result == null)
                    return;

                var newLoopStartSample = vmToneIdCreation.LoopStartSample;
                var newLoopEndSample = vmToneIdCreation.LoopEndSample;

                if (_audioImportService.IsNus3Audio(BgmPropertyViewModel.Filename))
                {
                    if (BgmPropertyViewModel.MusicPlayer != null)
                        await BgmPropertyViewModel.MusicPlayer.StopSong();

                    await UpdateNus3AudioLoopPointsWithProgress(
                        parentWindow,
                        BgmPropertyViewModel.NameId,
                        BgmPropertyViewModel.Filename,
                        newLoopStartSample,
                        newLoopEndSample
                    );

                    if (BgmPropertyViewModel.MusicPlayer != null)
                        await BgmPropertyViewModel.MusicPlayer.ChangeFilename(BgmPropertyViewModel.Filename);
                }

                BgmPropertyViewModel.LoopStartSample = vmToneIdCreation.LoopStartSample;
                BgmPropertyViewModel.LoopEndSample = vmToneIdCreation.LoopEndSample;
                BgmPropertyViewModel.LoopStartMs = vmToneIdCreation.LoopStartMs;
                BgmPropertyViewModel.LoopEndMs = vmToneIdCreation.LoopEndMs;
                BgmPropertyViewModel.TotalSamples = vmToneIdCreation.TotalSamples;
                BgmPropertyViewModel.TotalTimeMs = vmToneIdCreation.TotalTimeMs;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error while previewing and choosing loop points.");
                await _messageDialog.ShowError("Preview Loops", e.Message, e);
            }
            finally
            {
                DeleteTemporaryPreviewFile(BgmPropertyViewModel.Filename, previewFilename);
            }
        }

        private async Task UpdateNus3AudioLoopPointsWithProgress(
            Window parentWindow,
            string toneId,
            string filename,
            uint loopStartSample,
            uint loopEndSample)
        {
            var progressVm = new AudioConversionProgressModalWindowViewModel();
            progressVm.SetUpdatingLoops(Path.GetFileName(filename));

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

            var progressDialogTask = progressWindow.ShowDialog(parentWindow);

            try
            {
                await _audioImportService.UpdateExistingNus3AudioLoopPoints(toneId, filename, loopStartSample, loopEndSample);
                progressVm.SetComplete();
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

        private void DeleteTemporaryPreviewFile(string originalFilename, string previewFilename)
        {
            if (string.IsNullOrWhiteSpace(previewFilename) || string.Equals(originalFilename, previewFilename, StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                if (File.Exists(previewFilename))
                    File.Delete(previewFilename);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Could not delete temporary loop preview source file {Filename}.", previewFilename);
            }
        }

        private async Task NormalizeSong(Window parentWindow)
        {
            if (BgmPropertyViewModel == null)
                return;

            try
            {
                _logger.LogDebug("Clicked Normalize Song");

                if (string.IsNullOrWhiteSpace(BgmPropertyViewModel.Filename) || !File.Exists(BgmPropertyViewModel.Filename))
                {
                    await _messageDialog.ShowError("Normalize Song", "The song file could not be found.");
                    return;
                }

                var confirm = await _messageDialog.ShowWarningConfirm(
                    "Normalize Song",
                    $"This will normalize and overwrite '{Path.GetFileName(BgmPropertyViewModel.Filename)}'. Continue?"
                );

                if (!confirm)
                    return;

                if (BgmPropertyViewModel.MusicPlayer != null)
                    await BgmPropertyViewModel.MusicPlayer.StopSong();

                await NormalizeSongWithProgress(parentWindow);

                if (BgmPropertyViewModel.MusicPlayer != null)
                    await BgmPropertyViewModel.MusicPlayer.ChangeFilename(BgmPropertyViewModel.Filename);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error while normalizing song.");
                await _messageDialog.ShowError("Normalize Song", e.Message, e);
            }
        }

        private async Task NormalizeSongWithProgress(Window parentWindow)
        {
            var progressVm = new AudioConversionProgressModalWindowViewModel();
            progressVm.SetNormalizing(Path.GetFileName(BgmPropertyViewModel.Filename));

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

            var progressDialogTask = progressWindow.ShowDialog(parentWindow);

            try
            {
                await _audioImportService.NormalizeExistingNus3Audio(BgmPropertyViewModel.NameId, BgmPropertyViewModel.Filename);
                progressVm.SetComplete();
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

        private List<ComboItem> GetRecordTypes()
        {
            var recordTypes = new List<ComboItem>();
            recordTypes.AddRange(Constants.CONVERTER_RECORD_TYPE.Select(p => new ComboItem(p.Key, p.Value)));
            return recordTypes;
        }

        private List<ComboItem> GetSpecialCategories()
        {
            var recordTypes = new List<ComboItem>() { new ComboItem(string.Empty, "None/Other") };
            recordTypes.AddRange(Constants.SpecialCategories.UI_SPECIAL_CATEGORY.Select(p => new ComboItem(p.Key, p.Value)));
            return recordTypes;
        }

        private void SetSpecialCategoryRules(string specialRule)
        {
            if (!_isUpdatingSpecialRule)
            {
                _isUpdatingSpecialRule = true;
                IsSpecialCategoryPinch = false;

                if (_refSelectedItem != null)
                {
                    SelectedSpecialCategory = _specialCategories.FirstOrDefault(p => p.Id == specialRule);
                    if (SelectedSpecialCategory == null)
                        SelectedSpecialCategory = _specialCategories[0];
                    _refSelectedItem.StreamSetViewModel.SpecialCategory = specialRule;

                    switch (specialRule)
                    {
                        case Constants.SpecialCategories.SPECIAL_CATEGORY_PINCH_VALUE:
                            IsSpecialCategoryPinch = true;
                            break;
                    }
                }
                _isUpdatingSpecialRule = false;
            }
        }

        private void SetGameTitleId(GameTitleEntryViewModel gameTitle)
        {
            if (DbRootViewModel != null)
            {
                if (gameTitle != null)
                {
                    DbRootViewModel.UiGameTitleId = gameTitle.UiGameTitleId;
                }
                else
                    DbRootViewModel.UiGameTitleId = MusicConstants.InternalIds.GAME_TITLE_ID_DEFAULT;
            }
        }

        private void ClosingWindow(Window w)
        {
            BgmPropertyViewModel?.MusicPlayer?.ChangeFilename(_originalFilename);
        }

        protected override Task<bool> SaveChanges()
        {
            _logger.LogDebug("Save Changes");
            if (BgmPropertyViewModel.AudioVolume < Constants.MinimumGameVolume)
                BgmPropertyViewModel.AudioVolume = Constants.MinimumGameVolume;
            if (BgmPropertyViewModel.AudioVolume > Constants.MaximumGameVolume)
                BgmPropertyViewModel.AudioVolume = Constants.MaximumGameVolume;
            BgmPropertyViewModel.AudioVolume = (float)Math.Round(BgmPropertyViewModel.AudioVolume, 2, MidpointRounding.AwayFromZero);

            _originalFilename = BgmPropertyViewModel.Filename;

            DbRootViewModel.TestDispOrder = (short)(IsInSoundTest ? DbRootViewModel.TestDispOrder > -1 ? DbRootViewModel.TestDispOrder : _guiStateManager.GetNewHighestSoundTestOrderValue() : -1);
            if (SelectedRecordType != null)
                DbRootViewModel.RecordType = SelectedRecordType.Id;
            MSBTTitleEditor.SaveValueToRecent();
            MSBTAuthorEditor.SaveValueToRecent();
            MSBTCopyrightEditor.SaveValueToRecent();
            DbRootViewModel.MSBTTitle = SaveMSBTValues(MSBTTitleEditor.MSBTValues);
            DbRootViewModel.MSBTAuthor = SaveMSBTValues(MSBTAuthorEditor.MSBTValues);
            DbRootViewModel.MSBTCopyright = SaveMSBTValues(MSBTCopyrightEditor.MSBTValues);

            if (!string.IsNullOrEmpty(SelectedGameTitleViewModel?.UiGameTitleId) && _originalGameTitleId != SelectedGameTitleViewModel.UiGameTitleId)
            {
                AddRecentGameTitle(SelectedGameTitleViewModel);
            }

            return Task.FromResult(true);
        }

        private Dictionary<string, string> SaveMSBTValues(Dictionary<string, string> msbtValues)
        {
            var output = new Dictionary<string, string>();
            var copyToEmptyLocales = _config.CurrentValue.Sma5hMusicGUI.CopyToEmptyLocales;
            var defaultMSBTLocale = _config.CurrentValue.Sma5hMusicGUI.DefaultMSBTLocale;
            if (msbtValues != null)
            {
                foreach (var msbtValue in msbtValues)
                {
                    if (!string.IsNullOrEmpty(msbtValue.Value))
                        output.Add(msbtValue.Key, msbtValue.Value);
                    else if (copyToEmptyLocales && msbtValues.ContainsKey(defaultMSBTLocale))
                        output.Add(msbtValue.Key, msbtValues[defaultMSBTLocale]);
                }
            }
            return output;
        }

        protected override void LoadItem(BgmEntryViewModel item)
        {
            _logger.LogDebug("Load Item");
            DbRootViewModel = item?.DbRootViewModel;
            StreamSetViewModel = item?.StreamSetViewModel;
            AssignedInfoViewModel = item?.AssignedInfoViewModel;
            StreamPropertyViewModel = item?.StreamPropertyViewModel;
            BgmPropertyViewModel = item?.BgmPropertyViewModel;
            _originalFilename = BgmPropertyViewModel?.Filename;

            IsModSong = item.MusicMod != null;

            MSBTTitleEditor.MSBTValues = DbRootViewModel.MSBTTitle;
            MSBTAuthorEditor.MSBTValues = DbRootViewModel.MSBTAuthor;
            MSBTCopyrightEditor.MSBTValues = DbRootViewModel.MSBTCopyright;
            IsInSoundTest = DbRootViewModel.TestDispOrder > -1;
            SelectedRecordType = _recordTypes.FirstOrDefault(p => p.Id == DbRootViewModel.RecordType);
            SelectedGameTitleViewModel = _games.FirstOrDefault(p => p.UiGameTitleId == DbRootViewModel.UiGameTitleId);
            _originalGameTitleId = SelectedGameTitleViewModel?.UiGameTitleId;
            SetSpecialCategoryRules(StreamSetViewModel.SpecialCategory);
        }

        private async Task<bool> CalculateAudioCues(BgmPropertyEntryViewModel bgmPropertyEntryViewModel)
        {
            var audioCuePoints = await _guiStateManager.UpdateAudioCuePoints(bgmPropertyEntryViewModel.Filename);
            if (audioCuePoints != null)
            {
                _mapper.Map(audioCuePoints, bgmPropertyEntryViewModel);
                return true;
            }
            return false;
        }
    }
}
