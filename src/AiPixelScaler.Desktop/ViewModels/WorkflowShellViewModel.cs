namespace AiPixelScaler.Desktop.ViewModels;

public sealed class WorkflowShellViewModel
{
    public enum WorkflowStep { Importa, Pulisci, SliceAllinea, Esporta }

    public WorkflowStep ActiveStep { get; private set; } = WorkflowStep.Importa;

    public string StepSummary => ActiveStep switch
    {
        WorkflowStep.Importa => "Step 1/4 · Importa",
        WorkflowStep.Pulisci => "Step 2/4 · Pulisci",
        WorkflowStep.SliceAllinea => "Step 3/4 · Slice/Allinea",
        WorkflowStep.Esporta => "Step 4/4 · Esporta",
        _ => "Step 1/4 · Importa"
    };

    public string PrimaryActionLabel => ActiveStep switch
    {
        WorkflowStep.Importa => "Step 1 · Importa",
        WorkflowStep.Pulisci => "Step 2 · Pulisci",
        WorkflowStep.SliceAllinea => "Step 3 · Slice/Allinea",
        WorkflowStep.Esporta => "Step 4 · Esporta",
        _ => "Step 1 · Importa"
    };

    public void Select(WorkflowStep step) => ActiveStep = step;

    public void Advance()
    {
        ActiveStep = ActiveStep switch
        {
            WorkflowStep.Importa => WorkflowStep.Pulisci,
            WorkflowStep.Pulisci => WorkflowStep.SliceAllinea,
            WorkflowStep.SliceAllinea => WorkflowStep.Esporta,
            _ => WorkflowStep.Esporta
        };
    }

    public void UpdateFromReadiness(bool hasDocument, bool cleanApplied, bool hasCells)
    {
        if (!hasDocument)
        {
            ActiveStep = WorkflowStep.Importa;
            return;
        }

        if (!cleanApplied)
        {
            ActiveStep = WorkflowStep.Pulisci;
            return;
        }

        if (!hasCells)
        {
            ActiveStep = WorkflowStep.SliceAllinea;
            return;
        }

        ActiveStep = WorkflowStep.Esporta;
    }
}
