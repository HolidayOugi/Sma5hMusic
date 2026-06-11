using Microsoft.Extensions.Options;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using ReactiveUI.Validation.Extensions;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Models;
using System;
using System.IO;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Sma5hMusic.GUI.ViewModels
{
    public class GenerateModFromBuildFilesModalWindowViewModel : ModalBaseViewModel<ModEntryViewModel>
    {
        private const string RegexReplace = @"[^a-zA-Z0-9\-_ \[\]]";
        private const string RegexValidation = @"^[\w\-. \[\]]+$";
        private readonly IOptionsMonitor<ApplicationSettings> _config;

        [Reactive]
        public string ModName { get; set; }
        [Reactive]
        public string ModPath { get; set; }
        [Reactive]
        public string ModWebsite { get; set; }
        [Reactive]
        public string ModAuthor { get; set; }
        [Reactive]
        public string ModDescription { get; set; }
        [Reactive]
        public bool IsEdit { get; set; }

        public GenerateModFromBuildFilesModalWindowViewModel(IOptionsMonitor<ApplicationSettings> config)
        {
            _config = config;
            IsEdit = false;

            this.WhenAnyValue(p => p.ModName).Subscribe(p => FormatModPath(p));

            this.ValidationRule(p => p.ModName,
                p => !string.IsNullOrWhiteSpace(p),
                "Please enter a Title.");

            this.ValidationRule(p => p.ModPath,
                p => !string.IsNullOrWhiteSpace(p) &&
                     Regex.IsMatch(p, RegexValidation) &&
                     !Directory.Exists(Path.Combine(_config.CurrentValue.Sma5hMusic.ModPath, p)),
                "The folder name is invalid or the folder already exists.");
        }

        public MusicModInformation GetMusicModInformation()
        {
            return new MusicModInformation
            {
                Name = ModName,
                Author = ModAuthor,
                Website = ModWebsite,
                Description = ModDescription
            };
        }

        protected override void LoadItem(ModEntryViewModel item)
        {
            ModName = string.Empty;
            ModPath = string.Empty;
            ModWebsite = string.Empty;
            ModAuthor = string.Empty;
            ModDescription = string.Empty;
        }

        protected override Task<bool> SaveChanges()
        {
            return Task.FromResult(true);
        }

        private void FormatModPath(string modName)
        {
            ModPath = string.IsNullOrWhiteSpace(modName)
                ? string.Empty
                : Regex.Replace(modName, RegexReplace, string.Empty).ToLower();
        }
    }
}
