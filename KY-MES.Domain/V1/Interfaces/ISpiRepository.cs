using KY_MES.Domain.V1.DTOs.InputModels;

namespace KY_MES.Domain.V1.Interfaces
{
    public interface ISpiRepository
    {
        Task<long> SaveSpiRunAsync(string fert, InspectionRun run, List<InspectionUnitRecord> units, CancellationToken ct = default);
    }
}