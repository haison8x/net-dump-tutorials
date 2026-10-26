namespace CrashLab;

public sealed class OrderImporter
{
    public void ImportBatch(string fileName)
    {
        string[] lines = ["1001;Widget;3", "1002;Gadget;-1", "1003;Doohickey;2"];
        foreach (var line in lines)
            ImportLine(fileName, line);
    }

    private static void ImportLine(string fileName, string line)
    {
        var parts = line.Split(';');
        var quantity = int.Parse(parts[2]);
        if (quantity <= 0)
        {
            // Nobody catches this on a raw Thread -> the runtime terminates the process.
            throw new InvalidOperationException(
                $"Invalid quantity {quantity} for order {parts[0]} in {fileName}");
        }
    }
}
