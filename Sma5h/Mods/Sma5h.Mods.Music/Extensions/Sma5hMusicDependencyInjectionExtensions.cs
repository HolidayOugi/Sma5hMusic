using Microsoft.Extensions.Configuration;
using Sma5h.Interfaces;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.CskPackBuild;
using Sma5h.Mods.Music.Interfaces;
using Sma5h.Mods.Music.Models.AutoMapper;
using Sma5h.Mods.Music.Services;
using Sma5h.ResourceProviders;
using VGMMusic;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class Sma5hMusicDependencyInjectionExtensions
    {
        public static IServiceCollection AddSma5hMusic(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<Sma5hMusicOptions>(configuration);
            services.Configure<CskPackBuildOptions>(configuration);
            services.AddSingleton<ISma5hMod, Sma5hMusic>();
            services.AddSingleton<IResourceProvider, BgmPropertyProvider>();
            services.AddSingleton<IResourceProvider, MsbtResourceProvider>();
            services.AddSingleton<IResourceProvider, PrcResourceProvider>();
            services.AddSingleton<IMusicModManagerService, MusicModManagerService>();
            services.AddSingleton<IAudioStateService, AudioStateService>();
            //services.AddSingleton<IAudioMetadataService, VGAudioMetadataService>();
            services.AddSingleton<IAudioMetadataService, VGMStreamAudioMetadataService>();
            services.AddSingleton<IVGMMusicPlayer, VGMMusicPlayer>();
            services.AddSingleton<INus3AudioService, Nus3AudioService>();
            services.AddSingleton<IMusicModReverseService, MusicModReverseService>();
            services.AddSingleton<ICskPackBuildService, CskPackBuildService>();
            services.AddAutoMapper(_ => { }, typeof(MappingDb).Assembly, typeof(MappingMusicModConfig).Assembly);
            return services;
        }

        public static IServiceCollection AddSma5hMusicOverride(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<Sma5hMusicOverrideOptions>(configuration);
            services.AddSingleton<ISma5hMusicOverride, Sma5hMusicOverride>();
            services.AddSingleton<ISma5hMod, Sma5hMusicOverride>((o) => o.GetRequiredService<ISma5hMusicOverride>() as Sma5hMusicOverride);
            services.AddSingleton<IResourceProvider, PrcResourceProvider>();
            services.AddSingleton<IAudioStateService, AudioStateService>();
            services.AddAutoMapper(_ => { }, typeof(MappingDb).Assembly, typeof(MappingOverrideConfig).Assembly);
            return services;
        }
    }
}
