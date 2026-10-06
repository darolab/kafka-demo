using System;
namespace KafkaDemo.DTO;

public class Request
{
    // Private backing field
    private long _ts;

    // Public property
    public long Ts
    {
        get => _ts;

        set
        {
            _ts = value;

            // Validate whenever Ts is changed
            IsValid = _ts > 0;
        }
    }

    // Result of validation
    public bool IsValid { get; private set; }
}

class Program
{
    static void Main()
    {
        Dictionary<string, string> users = new Dictionary<string, string>
        {
            { "001", "Daro" },
            { "002", "John" },
            { "003", "David" }
        };
         Console.WriteLine($"AditonalParam: {users}");


        Request request = new Request();

        // This calls the Ts setter
        request.Ts = 1234567890;

        Console.WriteLine($"Timestamp: {request.Ts}");
        Console.WriteLine($"IsValid: {request.IsValid}");

        // Change the timestamp
        request.Ts = 0;

        Console.WriteLine($"Timestamp: {request.Ts}");
        Console.WriteLine($"IsValid: {request.IsValid}");
    }
}