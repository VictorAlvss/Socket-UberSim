using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace UberServer
{
    // Esta classe vai "segurar" o cliente e seus streams
    // ESTA CLASSE É CRUCIAL PARA A ARQUITETURA MULTITHREAD.
    // Ela resolve o problema de "quem é o dono" dos streams de rede.
    // Em vez de armazenar apenas o 'TcpClient' e criar 'StreamReader'/'StreamWriter' em vários lugares (o que "quebra" a conexão)
    // esta classe agrupa o cliente e seus streams em um ÚNICO objeto.

    public class ClientConnection
    {
        // A conexão TCP com o cliente.
        public TcpClient Client { get; }

        // O "leitor" de texto para este cliente, é usado pela Task 'HandleClient' para receber mensagens.
        public StreamReader Reader { get; }

        // O "escritor" de texto para este cliente. Será usado tanto pelo 'HandleClient' (para responder)
        public StreamWriter Writer { get; }

        public ClientConnection(TcpClient client)
        {
            // 1. Armazena o objeto cliente
            this.Client = client;

            // 2. Obtém o stream de rede (o "canal" de comunicação)
            NetworkStream stream = client.GetStream();

            // 3. Cria o Reader e o Writer UMA SÓ VEZ.  A partir de agora, qualquer parte do servidor que quiser
            // ler ou escrever para este cliente DEVE USAR estas
            // propriedades (Reader e Writer), em vez de criar novos.

            this.Reader = new StreamReader(stream, Encoding.UTF8);         
            this.Writer = new StreamWriter(stream, Encoding.UTF8) {
                // 'AutoFlush = true' é vital: Garante que cada 'WriteLineAsync' envie a mensagem imediatamente pela rede, sem esperar o buffer encher.
                AutoFlush = true 
            };
           
        }
    }
}
