using Sma5h.Mods.Music.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Sma5h.Mods.Music.Interfaces
{
    public interface ICskPackBuildService
    {
        Task Build(string locale = null);
        Task Build(IEnumerable<string> selectedSeriesKeys, string locale = null);
        Task BuildSingle(IEnumerable<string> selectedSeriesKeys, string locale = null);
        Task<IReadOnlyList<CskPackSeriesOption>> GetAvailableSeries(string locale = null);
    }
}
