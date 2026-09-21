# CatDog Rescue

Protótipo acadêmico 2D cooperativo para a disciplina de Fundamentos de Redes para Jogos Digitais.

## Tecnologias

- Unity 6.3.9f1
- Netcode for GameObjects 2.13.2
- Unity Transport 2.6.0, instalado como dependência do NGO
- Multiplayer Play Mode 2.0.2
- Unity Input System 1.18.0

## Arquitetura

O protótipo usa o modelo Host + Client:

- A primeira instância inicia como Host, ou seja, servidor e jogador Gato.
- A segunda instância conecta como Client, ou seja, jogador Cachorro.
- O Host mantém o estado oficial dos personagens, mecanismos, portão, resgate e conclusão da fase.
- O Client envia pedidos de ação; ele não altera diretamente o estado compartilhado.

## Estado Compartilhado

O Host sincroniza os seguintes estados por `NetworkVariable`:

- posição dos jogadores;
- mecanismo do Gato e do Cachorro;
- portão cooperativo;
- animal resgatado;
- conclusão da fase.

## Comunicação

| Informação | Estratégia |
| --- | --- |
| Movimento horizontal | RPC não confiável, pois o estado mais recente é mais relevante que comandos antigos. |
| Pulo | RPC confiável. |
| Ativar mecanismo | RPC confiável. |
| Resgate e conclusão | RPC confiável. |
| Conexão e desconexão | Callbacks do `NetworkManager`. |

O NGO e o Unity Transport abstraem os detalhes de transporte. O projeto não implementa TCP ou UDP manualmente.

## Controles

| Jogador | Mover | Pular | Mecanismo | Resgatar |
| --- | --- | --- | --- | --- |
| Gato / Host | A e D | W | E | Q |
| Cachorro / Client | Setas esquerda e direita | Seta para cima | Enter | Shift direito |

## Execução Local

1. Abra `Window > Multiplayer > Multiplayer Play Mode`.
2. Crie um Play Mode Scenario com duas instâncias:
   - Player 1: `Server and Client`;
   - Player 2: `Client`.
3. Entre em Play Mode.
4. Na primeira instância, clique em `Iniciar Host`.
5. Na segunda, clique em `Conectar como Client`.

## Roteiro de Demonstração

1. Mostre Gato e Cachorro aparecendo nas duas instâncias.
2. Mova cada personagem com seus próprios controles e mostre a posição sincronizada.
3. Faça os dois ativarem seus mecanismos. O Host abre o portão somente depois dos dois pedidos.
4. Aproxime qualquer personagem do animal à direita e execute o resgate.
5. Mostre a conclusão sincronizada da fase.
6. Feche uma instância para demonstrar a pausa e o retorno à tela inicial.
7. No Client, aumente os controles de atraso, jitter e perda para demonstrar efeitos em movimento.

## Simulação de Rede

Os sliders locais no painel de teste afetam apenas comandos de movimento:

- atraso: adia o envio de cada comando;
- jitter: varia esse atraso aleatoriamente;
- perda: descarta uma porcentagem de comandos antes de enviá-los.

Pulo, mecanismos e resgate não usam essa simulação. Isso demonstra a diferença entre atualização frequente e evento crítico.

Esta é uma simulação didática local, não uma medição nem emulação completa da rede física.