using UberServer;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System;


/*

Como não estamos usando HTTP, iremos definir nosso próprio protocolo de texto simples, usando o caractere | (pipe) para separar comandos e argumentos.

Mensagens do Cliente para o Servidor:

    MOTORISTA|ONLINE|Carlos|Placa-ABC1234

    MOTORISTA|ACEITAR|101 (onde 101 é o ID da corrida)

    MOTORISTA|FINALIZAR|101

    PASSAGEIRO|PEDIR|Maria|Rua A|Rua B

Mensagens do Servidor para o Cliente:

    SERVER|OK (Confirmação genérica)

    SERVER|ERRO|Mensagem de erro aqui

    SERVER|NOVA_CORRIDA|101|Rua A|Rua B (Enviado a todos os motoristas online)

    SERVER|CORRIDA_ACEITA|Carlos|Placa-ABC1234 (Enviado ao passageiro)

    SERVER|CORRIDA_REMOVIDA|101 (Enviado aos motoristas que não pegaram a corrida)
*/

namespace UberServer 
{
    class Program
    {
        // 1. DADOS COMPARTILHADOS (Thread-Safe)

        // Contador simples para IDs de corrida
        public static int corridaIdCounter = 0;

        // Dicionário de motoristas online. Chave = ID/Nome, Valor = Conexão TCP
        public static ConcurrentDictionary<string, ClientConnection> motoristasOnline = new();

        // Dicionário de corridas pendentes. Chave = ID Corrida, Valor = Objeto Corrida
        public static ConcurrentDictionary<int, Corrida> corridasPendentes = new();

        // Mapeia um passageiro à sua corrida= new();
        public static ConcurrentDictionary<int, ClientConnection> passageirosEsperando = new();

        static async Task Main(string[] args)
        {

            // 2. O LISTENER PRINCIPAL
            TcpListener listener = new TcpListener(IPAddress.Parse("127.0.0.1"), 8081);
            listener.Start();

            Console.WriteLine("Servidor Uber iniciado. Aguardando conexões...");
            
            while (true)
            {
                TcpClient client = await listener.AcceptTcpClientAsync();
                Console.WriteLine("Novo cliente conectado!");

                // Crie o objeto de conexão UMA VEZ
                ClientConnection connection = new ClientConnection(client);

                // Passe a CONEXÃO inteira para a Task
                _ = Task.Run(() => HandleClient(connection));
            }

        }

        async static Task HandleClient(ClientConnection connection)
        {

            TcpClient client = connection.Client;
            StreamReader reader = connection.Reader;
            StreamWriter writer = connection.Writer;

            string clientId = null;
            int? corridaAtivaId = null;

            try
            {
                while (client.Connected)
                {
                    string? message = await reader.ReadLineAsync();
                    if (message == null) break; // Cliente desconectou

                    Console.WriteLine($"Recebido: {message}");
                    string[] parts = message.Split('|');
                    string command = parts[0];
                    string tipoCliente = parts[1];

   

                    // 5. LÓGICA DO PROTOCOLO

                    // Define atráves da mensagem quem é o cliente do servidor que a enviou
                    // E partir da segunda parte protocolo, identifica qual operação o cliente deseja realizar

                    if (command == "MOTORISTA")
                    {
                        if (tipoCliente == "ONLINE")
                        {
                            clientId = $"{parts[2]}"; // ex: "Carlos"

                            motoristasOnline.TryAdd(clientId, connection);
                            await writer.WriteLineAsync("SERVER|OK|Você está online");

                            Console.WriteLine("-------------------------------------");
                            Console.WriteLine($"[LOG] Motorista '{clientId}' ficou online.");
                            Console.WriteLine($"Total de motoristas online: {motoristasOnline.Count}");
                            Console.WriteLine("Lista de Motoristas Online:");

                            // Itera sobre o dicionário e imprime a CHAVE (Key)
                            // A chave é o ID do motorista (ex: "Carlos")
                            foreach (var parMotorista in motoristasOnline)
                            {
                                Console.WriteLine($" -> {parMotorista.Key}");
                            }
                            Console.WriteLine("-------------------------------------");
                        }
                        else if (tipoCliente == "ACEITAR")
                        {
                            int corridaId = int.Parse(parts[2]);  
                            
                            if (corridaAtivaId.HasValue)
                            {
                                await writer.WriteLineAsync($"SERVER|ERRO|{clientId} ja está em uma corrida e não pode aceitar outra.");
                                continue;
                            } 

                            // 6. OPERAÇÃO ATÔMICA (Thread-Safe)
                            // Tenta "pegar" a corrida. Se conseguir, 'corrida' terá o valor.
                            if (corridasPendentes.TryRemove(corridaId, out Corrida corridaAceita))
                            {
                                await writer.WriteLineAsync($"SERVER|OK|Você aceitou a corrida {corridaId}");

                                // Notifica o passageiro
                                if (passageirosEsperando.TryRemove(corridaId, out ClientConnection passageiroConnection))
                                {
                                    // Pega o 'Writer' original do passageiro
                                    var pWriter = passageiroConnection.Writer;
                                    await pWriter.WriteLineAsync($"SERVER|CORRIDA {corridaId} ACEITA |{clientId}|Placa-XXXX");
                                }

                                corridaAtivaId = corridaId; // Lembre-se que este motorista está nesta corrida

                                // Tira o motorista da lista de disponíveis
                                motoristasOnline.TryRemove(clientId, out _);

                            }
                            else
                            {
                                // Mensagem que nos avisa se a corrida já foi finalizada ou está em andamento e bloqueia o motorista de aceitá-la
                                await writer.WriteLineAsync("SERVER|ERRO|Corrida não disponível");
                            }
                        }
                        else if (tipoCliente == "FINALIZAR")
                        {
                             // 1.VERIFICAÇÃO DE ESTADO
                             // Primeiro, verificamos se o motorista (esta thread)
                             // REALMENTE estava em uma corrida ('corridaAtivaId' tem um valor?)

                            if (corridaAtivaId.HasValue)
                                {
                                    int corridaFinalizadaId = corridaAtivaId.Value;

                                    // 2. AÇÃO: Limpa o estado "ocupado" deste motorista
                                    corridaAtivaId = null;

                                    // 3. CORREÇÃO DE TIPO: Adiciona o motorista de VOLTA à lista de disponíveis
                                    motoristasOnline.TryAdd(clientId, connection);

                                    // 4. FEEDBACK: Responde ao motorista
                                    await writer.WriteLineAsync($"SERVER|OK|Corrida {corridaFinalizadaId} finalizada. Voce esta disponivel novamente.");

                                    // 5. LOG DO SERVIDOR: Registra o que aconteceu
                                    Console.WriteLine("-------------------------------------");
                                    Console.WriteLine($"[LOG] Motorista '{clientId}' finalizou a corrida {corridaFinalizadaId}.");
                                    Console.WriteLine($"[LOG] Motorista '{clientId}' retornou a pool de disponiveis.");
                                    Console.WriteLine($"Total de motoristas online (disponíveis): {motoristasOnline.Count}");
                                    Console.WriteLine("-------------------------------------");

                                }
                                else
                                {
                                    // 6. TRATAMENTO DE ERRO: O motorista tentou finalizar,mas ele não estava em nenhuma corrida.
                                    await writer.WriteLineAsync("SERVER|ERRO|Voce nao esta em nenhuma corrida para finalizar.");
                                    Console.WriteLine($"[AVISO] Motorista '{clientId}' tentou finalizar uma corrida, mas nao estava em nenhuma.");
                                }
                            }
                    }
                    else if (command == "PASSAGEIRO")
                    {
                        // Verifica se o comando é de um PASSAGEIRO querendo 'PEDIR' uma corrida.

                        if (tipoCliente == "PEDIR")
                        {
                            clientId = $"Passageiro-{parts[2]}";

                            //Obtém um ID único para a nova corrida.
                            // Usamos 'Interlocked.Increment' em vez de 'corridaIdCounter++'.
                            // Isso é uma operação 'atômica' que impede a 'Race Condition':
                            // Se dois passageiros pedirem corridas no exato mesmo milissegundo,
                            // o Interlocked garante que eles receberão IDs diferentes (ex: 101 e 102)
                            // em vez de ambos receberem o mesmo ID.

                            int novaCorridaId = Interlocked.Increment(ref corridaIdCounter); // Thread-safe

                            // Armazena o ID da corrida que esta thread (HandleClient) está
                            // gerenciando na sua variável local 'corridaAtivaId
                            corridaAtivaId = novaCorridaId;

                            var novaCorrida = new Corrida
                            {
                                Id = novaCorridaId,
                                PassageiroId = clientId,
                                Origem = parts[3],
                                Destino = parts[4]
                            };

                            // Adiciona a corrida e o passageiro às listas de espera
                            corridasPendentes.TryAdd(novaCorridaId, novaCorrida);
                            passageirosEsperando.TryAdd(novaCorridaId, connection);
                            await writer.WriteLineAsync($"SERVER|OK|Procurando motorista para sua corrida {novaCorridaId}");

                            // 1. BROADCAST (Notifica todos os motoristas)
                            string msgCorrida = $"SERVER|NOVA_CORRIDA|{novaCorridaId}|{novaCorrida.Origem}|{novaCorrida.Destino}";

                            foreach (var parMotorista in motoristasOnline)
                            {
                                // Pega o 'Writer' que JÁ EXISTIA
                                StreamWriter motoristaWriter = parMotorista.Value.Writer;

                                try
                                {
                                    // Usa o writer original 
                                    await motoristaWriter.WriteLineAsync(msgCorrida);
                                }
                                catch (Exception ex)
                                {
                                    // Proteção caso o motorista tenha desconectado
                                    Console.WriteLine($"Erro ao notificar {parMotorista.Key}: {ex.Message}");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro: {ex.Message}");
            }
            finally
            {
                if (clientId != null)
                {
                    // 1. LIMPEZA DO MOTORISTA
                    // Se o cliente era um motorista, tenta removê-lo da lista de "disponíveis".
                    // Se ele não estava lá (ex: era passageiro ou estava em corrida),
                    // o 'TryRemove' apenas falha sem causar nenhum problema
                    motoristasOnline.TryRemove(clientId, out _);
                    Console.WriteLine("-------------------------------------");
                    Console.WriteLine($"[LOG] Motorista DISPONIVEL '{clientId}' desconectou.");
                    Console.WriteLine($"Total de motoristas online (disponíveis): {motoristasOnline.Count}");
                    Console.WriteLine("Lista de Motoristas Online (Restantes):");
                    foreach (var parMotorista in motoristasOnline)
                    {
                        Console.WriteLine($" -> {parMotorista.Key}");
                    }
                    Console.WriteLine("-------------------------------------");
                }

                // 2. LIMPEZA DE CORRIDAS 
                // Verifica se este cliente estava envolvido em alguma corrida
                if (corridaAtivaId.HasValue)
                {
                    int idParaLimpar = corridaAtivaId.Value;

                    // 2a. Tenta remover da lista de passageiros em espera.
                    // (Isso só vai funcionar se o cliente era um passageiro esperando)
                    passageirosEsperando.TryRemove(idParaLimpar, out _);

                    // 2b. Tenta remover da lista de corridas PENDENTES.
                    // Se um passageiro desconectar, a corrida é cancelada.
                    // A função 'TryRemove' retorna 'true' se conseguiu remover.
                    if (corridasPendentes.TryRemove(idParaLimpar, out Corrida corridaCancelada))
                    {
                        // A corrida foi removida com sucesso (era pendente e o passageiro saiu)
                        Console.WriteLine($"Corrida PENDENTE {idParaLimpar} (Passageiro: {corridaCancelada.PassageiroId}) foi cancelada devido à desconexão.");
                    }

                    writer.Close();
                    reader.Close();
                    client.Close();
                    Console.WriteLine($"Limpeza concluída. Conexão com {clientId ?? "desconhecido"} fechada.");

                }
            }
        }
        
    } 
}
