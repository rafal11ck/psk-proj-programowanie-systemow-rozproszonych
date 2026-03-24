using Grpc.Net.Client;
using password_break_server;

namespace ConsoleApp1
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            using var channel = GrpcChannel.ForAddress("https://localhost:7178");
            var client = new Greeter.GreeterClient(channel);

            Console.WriteLine("Połączono z serwerem gRPC.");
            Console.WriteLine("Wpisz wiadomość i naciśnij Enter. Wpisz 'exit', aby zakończyć.");

            while (true)
            {
                Console.Write("Ty: ");
                var text = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                if (text.Equals("exit", StringComparison.OrdinalIgnoreCase))
                    break;

                try
                {
                    var reply = await client.SayHelloAsync(new HelloRequest
                    {
                        Name = text
                    });

                    Console.WriteLine($"Serwer: {reply.Message}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Błąd połączenia: {ex.Message}");
                }
            }
        }
    }
}
