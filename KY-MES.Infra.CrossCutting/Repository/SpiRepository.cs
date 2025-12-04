using KY_MES.Domain.V1.DTOs.InputModels;
using KY_MES.Domain.V1.Interfaces;
using KY_MES.Infra.CrossCutting.Data;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.EntityFrameworkCore;

namespace KY_MES.Infra.CrossCutting;

public class SpiRepository : ISpiRepository
{
    private readonly AppDbContext _db;

    public SpiRepository(AppDbContext context)
    {
        _db = context;
    }

    public async Task<long> SaveSpiRunAsync(InspectionRun run, List<InspectionUnitRecord> units, CancellationToken ct = default)
    {
        // 0) checkagem se o run já existe no database
        var existingLogInDb = await _db.InspectionRuns
        .FirstOrDefaultAsync(x => x.InspectionBarcode == run.InspectionBarcode && x.Side == run.Side, ct);

        if (existingLogInDb != null)
        {
            return existingLogInDb.Id;
        }
    
        
        using var tx = await _db.Database.BeginTransactionAsync(ct);

        // 1) Salva o run
        _db.InspectionRuns.Add(run);
        await _db.SaveChangesAsync(ct);

        // 2) Converte DTOs
        var unitsEntities = new List<InspectionUnit>();
        foreach (var ur in units)
        {
            var u = new InspectionUnit
            {
                InspectionRunId = run.Id,
                ArrayIndex = ur.ArrayIndex,
                UnitBarcode = ur.UnitBarcode,
                Result = ur.Result,
                Side = ur.Side,
                Machine = ur.Machine,
                User = ur.User,
                StartTime = ur.StartTime,
                EndTime = ur.EndTime,
                ManufacturingArea = ur.ManufacturingArea,
                Carrier = run.Carrier
            };
            unitsEntities.Add(u);
        }
        _db.InspectionUnits.AddRange(unitsEntities);
        await _db.SaveChangesAsync(ct);

        // 3) Salva defeitos por unidade NG
        var unitByArray = unitsEntities.ToDictionary(x => x.ArrayIndex, x => x);
        var defectsToAdd = new List<InspectionDefect>();

        foreach (var ur in units)
        {
            if (ur.Defects == null || ur.Defects.Count == 0) continue;
            if (!unitByArray.TryGetValue(ur.ArrayIndex, out var unitEntity)) continue;

            foreach (var d in ur.Defects)
            {
                var def = new InspectionDefect
                {
                    InspectionUnitId = unitEntity.Id,
                    Comp = d.Comp,
                    Part = d.Part,
                    DefectCode = d.DefectCode,
                    Carrier = run.Carrier
                    
                };
                defectsToAdd.Add(def);
            }
        }

        if (defectsToAdd.Count > 0)
        {
            _db.InspectionDefects.AddRange(defectsToAdd);
            await _db.SaveChangesAsync(ct);
        }

        await tx.CommitAsync(ct);
        return run.Id;
            
    }
}
