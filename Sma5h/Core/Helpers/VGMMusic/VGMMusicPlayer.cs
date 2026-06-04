using Microsoft.Extensions.Logging;
using NAudio.Wave;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace VGMMusic
{
    public class VGMMusicPlayer : IVGMMusicPlayer, IDisposable
    {
        private readonly ILogger _logger;
        private VGMStreamReader _reader;
        private WaveOutEvent _outputDevice;
        private string _filename;
        private bool _requestStop;
        private float _volume;

        public int TotalTime { get { return _reader != null ? _reader.TotalSecondsToPlay : 0; } }
        public int CurrentTime { get { return _reader != null ? _reader.TotalPlayed : 0; } }
        public int CurrentSample { get { return _reader != null ? _reader.TotalPlayedSamples : 0; } }
        public bool Loaded { get { return _reader != null && _reader.FileLoaded; } }
        public bool ApplyVolume { get; set; }
        public float Volume { get { return _volume; } set { _volume = value; SetVolume(value); } }
        public bool IsPlaying { get; private set; }

        public VGMMusicPlayer(ILogger<IVGMMusicPlayer> logger)
        {
            _logger = logger;
            _volume = 0.8f;

        }

        public async Task<bool> LoadFile(string filename)
        {
            _logger.LogInformation("VGMMusicPlayer LoadFile requested. File={FileName}", filename);

            //Test file exist
            if (!File.Exists(filename))
            {
                _logger.LogError("Error while loading {FileName}, file doesn't exist.", filename);
                return false;
            }

            _logger.LogInformation("VGMMusicPlayer input exists. Length={Length}", new FileInfo(filename).Length);

            //Dispose current stream, if exist
            await Stop();

            //Attempt to load file
            _logger.LogInformation("Creating VGMStreamReader for {FileName}.", filename);
            _reader = new VGMStreamReader(filename);

            if (!_reader.FileLoaded)
            {
                _logger.LogError("Error while loading {FileName}. VGMStreamReader could not load the file. If this file plays on foobar make sure that libvgmstream is properly installed.", filename);
                return false;
            }

            _filename = filename;
            _logger.LogInformation("VGMStreamReader loaded. TotalSamples={TotalSamples}, TotalSamplesToPlay={TotalSamplesToPlay}, TotalSecondsToPlay={TotalSecondsToPlay}, LoopStart={LoopStart}, LoopEnd={LoopEnd}",
                _reader.TotalSamples, _reader.TotalSamplesToPlay, _reader.TotalSecondsToPlay, _reader.LoopStartSample, _reader.LoopEndSample);

            return true;
        }

        public async Task<VGMAudioCuePoints> GetAudioCuePoints(string filename)
        {
            await LoadFile(filename);
            var audioCuePoints = new VGMAudioCuePoints()
            {
                LoopEndMs = _reader.LoopEndMilliseconds,
                LoopEndSample = _reader.LoopEndSample,
                LoopStartMs = _reader.LoopStartMilliseconds,
                LoopStartSample = _reader.LoopStartSample,
                TotalTimeMs = _reader.TotalMilliseconds,
                TotalSamples = _reader.TotalSamples,
            };
            await Stop();
            return audioCuePoints;
        }

        public bool Play()
        {
            _logger.LogInformation("VGMMusicPlayer Play requested. Loaded={Loaded}, IsPlaying={IsPlaying}", Loaded, IsPlaying);

            if (!Loaded)
            {
                _logger.LogError("Error starting playback. The stream is not ready.");
                return false;
            }

            IsPlaying = true;
            Task.Run(() => { InternalPlay(); });
            _logger.LogInformation("VGMMusicPlayer playback task started for {FileName}.", _filename);

            return true;
        }

        public async Task<bool> Play(string filename)
        {
            _logger.LogInformation("VGMMusicPlayer Play file requested. File={FileName}", filename);

            if (await LoadFile(filename))
            {
                return Play();
            }
            return false;
        }

        public async Task<bool> Play(string filename, int startSample)
        {
            _logger.LogInformation("VGMMusicPlayer Play file from sample requested. File={FileName}, StartSample={StartSample}", filename, startSample);

            if (await LoadFile(filename))
            {
                _logger.LogInformation("Seeking preview to sample {StartSample}.", startSample);
                _reader.SeekToSample(startSample);
                _logger.LogInformation("Seek complete. CurrentPosition={Position}", _reader.Position);
                return Play();
            }
            return false;
        }

        private void SetVolume(float volume)
        {
            try
            {
                if (ApplyVolume && _outputDevice != null)
                    _outputDevice.Volume = volume;
            }
            catch (Exception e)
            {
                _logger.LogError(e.Message, "Error while setting volume: {Volume}", volume);
            }
        }

        public async Task<bool> Stop()
        {
            _logger.LogInformation("VGMMusicPlayer Stop requested. ReaderExists={ReaderExists}, IsPlaying={IsPlaying}, RequestStop={RequestStop}",
                _reader != null, IsPlaying, _requestStop);

            if (_reader != null && IsPlaying)
            {
                _requestStop = true;
                while (IsPlaying && _requestStop)
                {
                    await Task.Delay(200);
                }
            }
            else
            {
                InternalStop();
            }

            _logger.LogInformation("VGMMusicPlayer Stop completed. IsPlaying={IsPlaying}, RequestStop={RequestStop}", IsPlaying, _requestStop);
            return true;
        }

        private void InternalPlay()
        {
            try
            {
                _logger.LogInformation("InternalPlay starting. File={FileName}", _filename);
                _outputDevice = new WaveOutEvent();
                if (_reader != null)
                {
                    _logger.LogInformation("Initializing WaveOutEvent. WaveFormat={WaveFormat}, Position={Position}", _reader.WaveFormat, _reader.Position);
                    _outputDevice.Init(_reader);
                    if (ApplyVolume)
                        _outputDevice.Volume = Volume;
                    _outputDevice.Play();
                    _logger.LogInformation("WaveOutEvent started. PlaybackState={PlaybackState}, ApplyVolume={ApplyVolume}, Volume={Volume}", _outputDevice.PlaybackState, ApplyVolume, Volume);
                    while (_outputDevice.PlaybackState == PlaybackState.Playing && !_requestStop)
                    {
                        Thread.Sleep(500);
                    }
                    _logger.LogInformation("Playback loop ended. PlaybackState={PlaybackState}, RequestStop={RequestStop}", _outputDevice.PlaybackState, _requestStop);
                }
                _requestStop = false;

                /*using (var outputDevice = new WaveOutEvent())
                {
                    if (_reader != null)
                    {
                        outputDevice.Init(_reader);
                        outputDevice.Play();
                        IsPlaying = true;
                        while (outputDevice.PlaybackState == PlaybackState.Playing && !_requestStop)
                        {
                            Thread.Sleep(500);
                        }
                    }
                }
                _requestStop = false;*/
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error while initializing/playing the song {FileName}", _filename);
            }
            finally
            {
                InternalStop();
            }
        }

        private void InternalStop()
        {
            _logger.LogInformation("InternalStop starting. OutputDeviceExists={OutputDeviceExists}, ReaderExists={ReaderExists}", _outputDevice != null, _reader != null);
            _outputDevice?.Dispose();
            _outputDevice = null;
            _reader?.Dispose();
            _reader = null;
            _requestStop = false;
            IsPlaying = false;
            _logger.LogInformation("InternalStop completed.");
        }

        public void Dispose()
        {
            InternalStop();
        }
    }
}
