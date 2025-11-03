
using System.Net.Sockets;
using System.Text;


// A classe 'TcpClient' é a representação do "socket" do cliente.
TcpClient client = new TcpClient();
await client.ConnectAsync("127.0.0.1", 8081);
Console.WriteLine("Conectado ao servidor Uber!");

NetworkStream stream = client.GetStream();
StreamReader reader = new StreamReader(stream, Encoding.UTF8);
StreamWriter writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

// 1. TASK OUVINTE (Thread separada)
// Inicia uma Task SÓ para ficar lendo mensagens do servidor

Task.Run(async () =>
{
    try
    {
        while (client.Connected)
        {
            string? serverMessage = await reader.ReadLineAsync();
            if (serverMessage == null) break;

            // Apenas exibe a notificação do servidor
            Console.WriteLine($"\n[Servidor]: {serverMessage}\nDigite seu comando:\n");
        }
    }

    catch { Console.WriteLine("Desconectado."); }
});

// 2. LOOP REMETENTE (Thread principal)
// O loop principal fica lendo o que o usuário digita

Console.WriteLine("Digite 'online [SeuNome]' para ficar online.\n");

while (client.Connected)
{
    string? userInput = Console.ReadLine();
    if (string.IsNullOrEmpty(userInput)) continue;

    // Lógica para formatar a mensagem para o nosso protocolo
    if (userInput.StartsWith("online"))
    {
        string nome = userInput.Split(' ')[1];
        await writer.WriteLineAsync($"MOTORISTA|ONLINE|{nome}|Placa-{GetRandomPlateNumber()}");
    }
    else if (userInput.StartsWith("aceitar"))
    {
        string corridaId = userInput.Split(' ')[1];
        await writer.WriteLineAsync($"MOTORISTA|ACEITAR|{corridaId}|");
    }
    else if (userInput.StartsWith("finalizar"))
    {
        string corridaId = userInput.Split(' ')[1];
        await writer.WriteLineAsync($"MOTORISTA|FINALIZAR|{corridaId}");
    }
}


 static string  GetRandomPlateNumber()
{
    Random random = new Random();
    char[] letters = new char[3];
    int numberPart = random.Next(0, 10000);

    for (int i = 0; i < 3; i++)
    {
        // Letras maiúsculas de A (65) a Z (90)
        letters[i] = (char)random.Next('A', 'Z' + 1);
    }

    // Formata com 4 dígitos, incluindo zeros à esquerda
    return $"{new string(letters)}{numberPart:D4}";
}

