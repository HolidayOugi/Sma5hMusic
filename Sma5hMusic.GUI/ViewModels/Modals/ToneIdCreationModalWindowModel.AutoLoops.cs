using Microsoft.Extensions.Logging;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Sma5hMusic.GUI.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace Sma5hMusic.GUI.ViewModels
{
    public partial class ToneIdCreationModalWindowModel
    {
        private const int AutoLoopPageSize = 15;
        private IDisposable _autoLoopStatusSubscription;
        private List<AutoLoopPoint> _allAutoLoopPoints = new List<AutoLoopPoint>();

        public ReactiveCommand<Unit, Unit> ActionCalculateAutoLoops { get; }
        public ReactiveCommand<Unit, Unit> ActionLoadMoreAutoLoops { get; }
        public ReactiveCommand<AutoLoopPoint, Unit> ActionPreviewAutoLoop { get; }

        [Reactive]
        public bool IsCalculatingAutoLoops { get; set; }

        [Reactive]
        public bool IsAutoLoopCandidatesVisible { get; set; }

        [Reactive]
        public AutoLoopPoint SelectedAutoLoop { get; set; }

        [Reactive]
        public string AutoLoopStatus { get; set; }

        [Reactive]
        public bool HasMoreAutoLoops { get; set; }

        public ObservableCollection<AutoLoopPoint> AutoLoopPoints { get; }

        private async Task CalculateAutoLoops()
        {
            try
            {
                _logger.LogInformation("Calculate automatic loop points clicked. Filename={Filename}, SampleRate={SampleRate}, TotalSamples={TotalSamples}.",
                    Filename, SampleRate, TotalSamples);

                await StopPreview();
                ClearAutoLoopPoints();
                IsCalculatingAutoLoops = true;
                StartAutoLoopStatusAnimation();

                var loopPoints = await _audioImportService.CalculateAutoLoopPoints(Filename, SampleRate, TotalSamples);
                if (loopPoints.Count == 0)
                {
                    StopAutoLoopStatusAnimation();
                    IsCalculatingAutoLoops = false;

                    _logger.LogInformation("pymusiclooper did not return any valid loop points for {Filename}.", Filename);
                    AutoLoopStatus = "No automatic loop points found.";
                    return;
                }

                _allAutoLoopPoints = loopPoints.ToList();
                LoadMoreAutoLoops();

                IsAutoLoopCandidatesVisible = true;
                UpdateAutoLoopLoadedStatus();
                _logger.LogInformation("Automatic loop point candidates loaded into modal. Count={Count}.",
                    _allAutoLoopPoints.Count);
            }
            catch (FileNotFoundException e)
            {
                StopAutoLoopStatusAnimation();
                IsCalculatingAutoLoops = false;

                _logger.LogError(e, "pymusiclooper is not available.");
                AutoLoopStatus = string.Empty;

                await _messageDialog.ShowError(
                    "pymusiclooper not found",
                    "pymusiclooper was not found. Please install it and add it to PATH.",
                    e
                );
            }
            catch (Exception e)
            {
                StopAutoLoopStatusAnimation();
                IsCalculatingAutoLoops = false;

                _logger.LogError(e, "Automatic loop point calculation failed.");
                AutoLoopStatus = string.Empty;

                await _messageDialog.ShowError(
                    "Automatic loop point calculation failed",
                    e.Message,
                    e
                );
            }
            finally
            {
                StopAutoLoopStatusAnimation();
                IsCalculatingAutoLoops = false;
            }
        }

        private async Task PreviewAutoLoop(AutoLoopPoint loopPoint)
        {
            if (loopPoint == null)
                return;

            _logger.LogInformation("Automatic loop preview clicked. Rank={Rank}, Start={Start}, End={End}, Score={Score}.",
                loopPoint.Rank, loopPoint.LoopStartSample, loopPoint.LoopEndSample, loopPoint.Score);

            SelectedAutoLoop = loopPoint;
            ApplyAutoLoop(loopPoint);
            await PreviewLoop();
        }

        private void ApplyAutoLoop(AutoLoopPoint loopPoint)
        {
            if (loopPoint == null)
                return;

            _logger.LogInformation("Applying automatic loop point. Rank={Rank}, Start={Start}, End={End}, Score={Score}.",
                loopPoint.Rank, loopPoint.LoopStartSample, loopPoint.LoopEndSample, loopPoint.Score);

            LoopStartSample = loopPoint.LoopStartSample;
            LoopEndSample = loopPoint.LoopEndSample;
            AutoLoopStatus = $"Selected automatic loop #{loopPoint.Rank} ({loopPoint.ScoreText}).";
        }

        private void ClearAutoLoopPoints()
        {
            _allAutoLoopPoints.Clear();
            AutoLoopPoints.Clear();
            SelectedAutoLoop = null;
            IsAutoLoopCandidatesVisible = false;
            HasMoreAutoLoops = false;
            AutoLoopStatus = string.Empty;
        }

        private void LoadMoreAutoLoops()
        {
            var nextLoopPoints = _allAutoLoopPoints
                .Skip(AutoLoopPoints.Count)
                .Take(AutoLoopPageSize)
                .ToList();

            foreach (var loopPoint in nextLoopPoints)
                AutoLoopPoints.Add(loopPoint);

            HasMoreAutoLoops = AutoLoopPoints.Count < _allAutoLoopPoints.Count;
            IsAutoLoopCandidatesVisible = AutoLoopPoints.Count > 0;
            UpdateAutoLoopLoadedStatus();
            _logger.LogInformation("Loaded automatic loop point page. Visible={VisibleCount}, Total={TotalCount}, HasMore={HasMore}.",
                AutoLoopPoints.Count, _allAutoLoopPoints.Count, HasMoreAutoLoops);
        }

        private void UpdateAutoLoopLoadedStatus()
        {
            if (_allAutoLoopPoints.Count > 0)
                AutoLoopStatus = $"Showing {AutoLoopPoints.Count} of {_allAutoLoopPoints.Count} automatic loop point candidate(s).";
        }

        private void StartAutoLoopStatusAnimation()
        {
            StopAutoLoopStatusAnimation();
            UpdateAutoLoopStatusAnimation(0);
            _autoLoopStatusSubscription = Observable.Interval(TimeSpan.FromSeconds(1))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(p => UpdateAutoLoopStatusAnimation((int)((p + 1) % 4)));
        }

        private void StopAutoLoopStatusAnimation()
        {
            _autoLoopStatusSubscription?.Dispose();
            _autoLoopStatusSubscription = null;
        }

        private void UpdateAutoLoopStatusAnimation(int dotCount)
        {
            AutoLoopStatus = $"Calculating automatic loop points{new string('.', dotCount)}";
        }
    }
}
