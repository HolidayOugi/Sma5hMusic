using Sma5hMusic.GUI.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Sma5hMusic.GUI.Interfaces
{
    public interface ICskPackBuildService
    {
        Task Build();
        Task Build(IEnumerable<string> selectedSeriesKeys);
        Task<IReadOnlyList<CskPackSeriesOption>> GetAvailableSeries();
    }
}
