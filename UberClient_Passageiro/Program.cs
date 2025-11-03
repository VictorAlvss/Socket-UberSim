
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

            // Exibe qualquer notificação do servidor
            Console.WriteLine($"\n[Servidor]: {serverMessage}\n");
        }
    }
    catch { Console.WriteLine("Desconectado do servidor."); }
});

// 2. Loop principal para enviar comandos do usuário
Console.WriteLine("Digite 'pedir [origem] [destino]' para pedir uma corrida.\n");

while (client.Connected)
{
    string? userInput = Console.ReadLine();
    if (string.IsNullOrEmpty(userInput)) continue;

    // Lógica simples para formatar a mensagem para o nosso protocolo
    if (userInput.StartsWith("pedir"))
    {
        // Ex: "pedir RuaA RuaB"
        string[] parts = userInput.Split(' ');
        string origem = parts[1];
        string destino = parts[2];

        // Envia no formato do protocolo: "PASSAGEIRO|PEDIR|Eu|RuaA|RuaB"
        await writer.WriteLineAsync($"PASSAGEIRO|PEDIR|Passageiro|{origem}|{destino}");
    }

}