# Simulador de Corridas em C# com Sockets TCP

![Linguagem](https://img.shields.io/badge/Linguagem-C%23-blue.svg)
![Plataforma](https://img.shields.io/badge/Plataforma-Console-lightgrey.svg)
![Rede](https://img.shields.io/badge/Rede-Sockets%20TCP-orange.svg)
![Multithreading](https://img.shields.io/badge/Multithreading-Task%20%26%20ConcurrentDictionary-blueviolet.svg)

Projeto acadêmico que simula a arquitetura de back-end de um aplicativo de corridas (como Uber), construído inteiramente em C# e rodando no console. O desafio central foi criar um sistema cliente-servidor do zero, usando **Sockets TCP de baixo nível** em vez de APIs web prontas (como REST ou SignalR).

O foco do projeto é demonstrar os fundamentos de **programação de rede**, **multithreading** e **gerenciamento de estado seguro** (thread-safety).

## Demonstração em Vídeo

*(Opcional: Coloque o link do seu vídeo do YouTube aqui!)*

`[Link para a demonstração no YouTube]`

## Arquitetura do Sistema

O sistema é composto por **3 projetos de console** independentes que rodam simultaneamente:

1.  **`UberServer`**: O cérebro. Um servidor TCP multithread que gerencia o estado e coordena a comunicação.
2.  **`UberClient_Motorista`**: O aplicativo do motorista. Permite ficar online, receber e aceitar/finalizar corridas.
3.  **`UberClient_Passageiro`**: O aplicativo do passageiro. Permite solicitar corridas e receber atualizações em tempo real.


## Principais Funcionalidades

* **Conexões Múltiplas:** O servidor aceita múltiplos motoristas e passageiros ao mesmo tempo.
* **Estado do Motorista:** Motoristas podem ficar `ONLINE` (disponíveis) e são removidos da pool ao `ACEITAR` uma corrida.
* **Ciclo de Corrida Completo:**
    1.  Passageiro envia `PEDIR`.
    2.  Servidor faz "broadcast" (notifica) da nova corrida para **todos** os motoristas `ONLINE`.
    3.  Um motorista envia `ACEITAR`.
    4.  Servidor valida (usando `ConcurrentDictionary.TryRemove`), notifica o passageiro e remove o motorista da pool.
    5.  Motorista envia `FINALIZAR`.
    6.  Servidor notifica o passageiro e coloca o motorista de volta na pool de `ONLINE`.
* **Tolerância a Falhas:** O servidor lida com desconexões abruptas de clientes (no bloco `finally`), removendo-os das listas de estado e cancelando corridas pendentes.

## Destaques Técnicos

O projeto foi construído sobre alguns pilares técnicos fundamentais:

### 1. Servidor Multithread (`UberServer`)

O coração do servidor é o seu "recepcionista" no `Main`:

```csharp
while (true)
{
    TcpClient client = await listener.AcceptTcpClientAsync();
    ClientConnection connection = new ClientConnection(client);
    
    // Delega o cliente para uma thread separada
    _ = Task.Run(() => HandleClient(connection));
}

-   `listener.AcceptTcpClientAsync()`: Aguarda (sem bloquear) por uma nova conexão.
-   `Task.Run()`: Delega o `HandleClient` (que contém o loop de vida daquele cliente) para o **Thread Pool** do .NET. Isso permite que o `while(true)` volte imediatamente para aceitar o próximo cliente, alcançando alta concorrência.
```
### 2. Gerenciamento de Estado Thread-Safe

Para evitar "Race Conditions" (condições de corrida), onde duas threads tentam modificar o mesmo dado ao mesmo tempo, o estado do servidor é gerenciado por coleções seguras:

-   **`ConcurrentDictionary`**: Usado para todas as listas de estado (`motoristasOnline`, `corridasPendentes`, `passageirosEmCorrida`). Seus métodos (como `TryAdd`, `TryRemove`, `TryGetValue`) são atômicos e garantem que apenas uma thread por vez modifique a coleção.
-   **`Interlocked.Increment`**: Usado para gerar IDs de corrida (`corridaIdCounter`). Garante que, mesmo que dois passageiros peçam corridas no exato mesmo milissegado, eles receberão IDs únicos.

### 3. Protocolo de Comunicação Customizado

Toda a comunicação é feita por um protocolo de texto simples baseado em `|` (pipe):

-   **Cliente envia:** `COMANDO|TIPO|ARG1|ARG2...`
    -   `MOTORISTA|ONLINE|Carlos`
    -   `PASSAGEIRO|PEDIR|Rua A|Rua B`
    -   `MOTORISTA|ACEITAR|101`
-   **Servidor envia:** `SERVER|TIPO|MENSAGEM...`
    -   `SERVER|OK|Voce esta online`
    -   `SERVER|NOVA_CORRIDA|102|Rua C|Rua D`
    -   `SERVER|CORRIDA_ACEITA|Carlos|Placa-ABC`

### 4. Arquitetura de Conexão (Classe `ClientConnection`)

Para evitar bugs onde StreamReader e StreamWriter são criados múltiplas vezes para o mesmo NetworkStream (o que "quebra" a conexão), foi criada a classe ClientConnection.
Ela "envelopa" o TcpClient e seus Reader/Writer no momento da conexão.
Esse objeto ClientConnection é criado uma única vez e é o que é salvo nos dicionários, garantindo que o servidor sempre use os streams corretos para se comunicar.
