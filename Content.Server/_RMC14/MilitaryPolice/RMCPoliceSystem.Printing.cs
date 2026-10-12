using System.Linq;
using System.Text;
using Content.Shared._RMC14.MilitaryPolice;
using Content.Shared.StationRecords;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._RMC14.MilitaryPolice;

public sealed partial class RMCPoliceSystem
{
    private EntityUid? Print(Entity<RMCPoliceConsoleComponent> ent, EntityUid actor,
        EntProtoId prototype, string name, string content)
    {
        if (_timing.CurTime < ent.Comp.NextPrint)
            return null;

        ent.Comp.NextPrint = _timing.CurTime + TimeSpan.FromSeconds(1.5);
        var paper = Spawn(prototype, Transform(ent).Coordinates);
        _paper.SetContent(paper, FormattedMessage.EscapeText(content));
        var metadata = MetaData(paper);
        EntityManager.System<MetaDataSystem>().SetEntityName(paper,
            Loc.GetString(ent.Comp.Sentencing ? "rmc-police-incident-print-title" : "rmc-police-print-title",
                ("name", name)), metadata);
        _audio.PlayPvs(ent.Comp.PrintSound, ent);
        LogChange(actor, ent, $"printed {name}");
        Refresh(ent);
        return paper;
    }

    private string FormatRecord(GeneralStationRecord general, RMCPoliceRecord record)
    {
        var content = new StringBuilder(Loc.GetString("rmc-police-record-print",
            ("name", general.Name), ("id", general.Fingerprint ?? ""),
            ("rank", general.JobTitle), ("age", general.Age), ("squad", general.Squad ?? ""),
            ("gender", Loc.GetString($"rmc-police-gender-{general.Gender}")),
            ("physical", Loc.GetString($"rmc-police-physical-{record.PhysicalStatus}")),
            ("mental", Loc.GetString($"rmc-police-mental-{record.MentalStatus}")),
            ("status", Loc.GetString($"rmc-police-status-{record.Status}")),
            ("crimes", record.MajorCrimes)));
        foreach (var comment in record.Comments)
        {
            content.AppendLine().AppendLine(comment.Author).AppendLine(comment.CreatedAt.ToString(@"hh\:mm\:ss"));
            content.AppendLine(comment.DeletedBy == null ? comment.Text :
                Loc.GetString("rmc-police-comment-deleted", ("author", comment.DeletedBy),
                    ("time", comment.DeletedAt?.ToString(@"hh\:mm\:ss") ?? "")));
        }
        foreach (var incident in record.Incidents)
            content.AppendLine().AppendLine(FormatIncident(incident));
        return content.ToString();
    }

    private string FormatIncident(RMCIncidentReport report)
    {
        report.TryCalculateSentence(_prototypes, EntityManager.ComponentFactory, out var calculated);
        var time = (report.FinalSentence ?? calculated).GetDescription();
        var content = new StringBuilder(Loc.GetString("rmc-police-incident-print",
            ("name", report.Suspect), ("sentence", time), ("summary", report.Summary)));
        if (report.Detention is { } detention)
            content.AppendLine(detention.GetDescription());
        content.AppendLine().AppendLine(Loc.GetString("rmc-police-incident-charges"));
        foreach (var charge in report.Charges)
        {
            if (!RMCMarineLaw.TryGet(_prototypes, EntityManager.ComponentFactory, charge.Law, out var law))
                continue;
            content.AppendLine().AppendLine(law.Name);
            content.AppendLine(RMCMarineSentence.DescribePunishmentField(law.Punishment, law.BrigTime));
            if (charge.RelatedCrime is { } related && RMCMarineLaw.TryGet(_prototypes, EntityManager.ComponentFactory, related, out var relatedLaw))
                content.AppendLine(Loc.GetString("rmc-police-related-crime-value", ("crime", relatedLaw.Name)));
            if (law.SpecialPunishment is { } punishment)
                content.AppendLine(Loc.GetString(punishment));
            if (law.Conditions is { } conditions)
                content.AppendLine(Loc.GetString(conditions));
        }
        content.AppendLine().AppendLine(Loc.GetString("rmc-police-witnesses"));
        foreach (var witness in report.Witnesses)
            content.AppendLine(witness.Name).AppendLine(witness.Notes);
        content.AppendLine().AppendLine(Loc.GetString("rmc-police-evidence"));
        foreach (var evidence in report.Evidence)
            content.AppendLine(evidence.Name).AppendLine(evidence.Notes);
        return content.ToString();
    }

    public void SynchronizeIncidentReport(Entity<RMCIncidentReportPaperComponent> paper)
    {
        if (paper.Comp.Report is not { } report)
            return;

        _paper.SetContent(paper, FormattedMessage.EscapeText(FormatIncident(report)));
        if (report.RecordId is not { } id || report.RecordStation is not { } station ||
            !TryGetEntity(station, out var stationUid))
            return;

        var key = new StationRecordKey(id, stationUid.Value);
        if (!_records.TryGetRecord<RMCPoliceRecord>(key, out var record))
            return;

        var incident = record.Incidents.FirstOrDefault(entry => entry.Id == report.Id);
        if (incident == null)
            return;
        incident.Detention = report.Detention;
        _records.Synchronize(key);
    }
}
