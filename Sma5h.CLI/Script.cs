using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sma5h.Interfaces;
using Sma5h.Mods.Music.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Sma5h.CLI
{
    public class Script
    {
        private readonly ILogger _logger;
        private readonly IConfiguration _configuration;
        private readonly IStateManager _state;
        private readonly IServiceProvider _serviceProvider;
        private readonly IWorkspaceManager _workspace;
        private const double CLIVersion = 1.41;

        public Script(IServiceProvider serviceProvider, IConfiguration configuration, IWorkspaceManager workspace, IStateManager state, ILogger<Script> logger)
        {
            _serviceProvider = serviceProvider;
            _configuration = configuration;
            _workspace = workspace;
            _state = state;
            _logger = logger;
        }

        public async Task Run()
        {
            _logger.LogInformation($"Sma5h.CLI v.{CLIVersion}");
            _logger.LogInformation("--------------------");
            _logger.LogInformation("research: soneek");
            _logger.LogInformation("prcEditor: https://github.com/BenHall-7/paracobNET");
            _logger.LogInformation("msbtEditor: https://github.com/IcySon55/3DLandMSBTeditor");
            _logger.LogInformation("nus3audio:  https://github.com/jam1garner/nus3audio-rs");
            _logger.LogInformation("bgm-property:  https://github.com/jam1garner/smash-bgm-property");
            _logger.LogInformation("VGAudio:  https://github.com/Thealexbarney/VGAudio");
            _logger.LogInformation("--------------------");

            await Task.Delay(1000);

            if (string.Equals(_configuration.GetValue<string>("Mode"), "ReverseMusicMod", StringComparison.OrdinalIgnoreCase))
            {
                ReverseMusicMod();
                return;
            }

            //Init State Manager
            _state.Init();

            //Init workspace
            if (!_workspace.Init())
                return;

            //Load Mods
            var mods = _serviceProvider.GetServices<ISma5hMod>();

            //Step that initialize a mod
            //Execute different checks to ensure the mod can run
            //Load resource files
            //Load existing mods
            _logger.LogInformation("--------------------");
            var initMods = new List<ISma5hMod>();
            foreach (var mod in mods)
            {
                _logger.LogInformation("{ModeName}: Initialize mod", mod.ModName);
                if (mod.Init())
                    initMods.Add(mod);
            }

            //Step to activate an eventual build step for a mod.
            //In UI, this would represent a user building a mod for ARC
            //Note: All resources managed by State Manager will be build at the end of this phase
            _logger.LogInformation("--------------------");
            foreach (var mod in initMods)
            {
                _logger.LogInformation("{ModeName}; Build mod changes", mod.ModName);
                mod.Build();
            }

            //Generate Output mod
            //All resources managed by State Manager are built
            _logger.LogInformation("--------------------");
            _logger.LogInformation("Starting State Manager Mod Generation");
            _state.WriteChanges();
            _logger.LogInformation("COMPLETE - Please check the logs for any error.");
            _logger.LogInformation("--------------------");
        }

        private void ReverseMusicMod()
        {
            var coreResourcesPath = _configuration.GetValue<string>("ReverseMusicMod:CoreResourcesPath");
            var outputPath = _configuration.GetValue<string>("ReverseMusicMod:OutputPath");
            var modOutputPath = _configuration.GetValue<string>("ReverseMusicMod:ModOutputPath");
            var modName = _configuration.GetValue<string>("ReverseMusicMod:Name");

            _logger.LogInformation("ReverseMusicMod: CoreResourcesPath={CoreResourcesPath}", coreResourcesPath);
            _logger.LogInformation("ReverseMusicMod: OutputPath={OutputPath}", outputPath);
            _logger.LogInformation("ReverseMusicMod: ModOutputPath={ModOutputPath}", modOutputPath);

            var reverseService = _serviceProvider.GetRequiredService<IMusicModReverseService>();
            reverseService.Reverse(coreResourcesPath, outputPath, modOutputPath, modName);

            _logger.LogInformation("ReverseMusicMod complete.");
        }
    }
}
