using Sma5h.Mods.Music.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Sma5h.Mods.Music.Interfaces
{
    public interface ICskPackBuildService
    {
        Task Build();
        Task Build(IEnumerable<string> selectedSeriesKeys);
        Task BuildSingle(IEnumerable<string> selectedSeriesKeys);
        Task<IReadOnlyList<CskPackSeriesOption>> GetAvailableSeries();
    }
}
