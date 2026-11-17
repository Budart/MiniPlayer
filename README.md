# MiniPlayer

Mini player de mídia que se encaixa na barra de tarefas do Windows 10/11, com espectro de áudio e a bateria dos seus dispositivos sem fio.

## Download e instalação
1. Baixe o **MiniPlayer.exe** em [Releases](../../releases/latest).
2. Abra o arquivo. Não precisa instalar nada: ele é portátil e pode ficar em qualquer pasta.
3. O player aparece na barra de tarefas. Para fechar, use o menu do botão direito.

> Na primeira execução o Windows SmartScreen pode avisar sobre "editor desconhecido": clique em **Mais informações → Executar assim mesmo**.

## O que ele faz
- **Controle de qualquer mídia**: voltar, play/pause e avançar, com capa, título e artista do que estiver tocando no PC (Spotify, Tidal, YouTube e outros sites no navegador, players de vídeo...).
- **Espectro de áudio** discreto atrás dos botões, na cor predominante da capa.
- **Bateria dos dispositivos** em tempo real, com ícone por tipo e um raio quando está carregando (passe o mouse para ver o nome):
  - tudo que o Windows informa via Bluetooth: fones, controles Xbox, teclados, mouses...
  - DualSense e DualShock 4 (Bluetooth ou cabo);
  - Logitech (receptores Lightspeed, Unifying e Bolt), Razer sem fio, Nintendo Pro Controller e Joy-Con;
  - mouses e teclados 2.4 GHz que seguem o padrão HID de bateria.
- **Some sozinho** em jogos e vídeos em tela cheia, e volta ao sair.

## Menu (botão direito no player ou no ícone da bandeja)
O MiniPlayer também fica com um ícone na bandeja do sistema (perto do relógio; se não aparecer, procure na seta **^**). Ele tem as mesmas opções do menu do player.

| Menu | Opção | O que faz |
|---|---|---|
| **Posição** | Fixo na barra de tarefas | Encaixa o player na barra (padrão). |
| | Solto na tela (arrastar) | Desprende o player: arraste-o para onde quiser. A posição é lembrada. |
| | Layout vertical (solto) | ✓ = player solto em coluna (botões, capa e baterias empilhados). |
| | Atrás das janelas (solto) | ✓ = fica atrás das janelas, como um widget da área de trabalho. Sobre a barra de tarefas e no **Win+D** ele continua visível. |
| **Exibir** | Controles de mídia | ✓ = mostra botões, capa, título e espectro. |
| | Bateria dos dispositivos | ✓ = mostra os indicadores de bateria. Um dos dois sempre fica visível. |
| **Tema** | 10 opções | Cores do player: Padrão, Escuro, Claro, Neon, Pôr do sol, Oceano, Floresta, Púrpura, Rubi e Alto contraste. |
| | Iniciar com o Windows | ✓ = abre o MiniPlayer ao ligar o PC. |
| | Encerrar MiniPlayer | Fecha o app. |

Em todas as opções, **✓ significa ativado**; nas escolhas de posição e tema, o ✓ indica a opção atual.

## Posição na barra de tarefas (modo fixo)
O encaixe segue o alinhamento da barra do Windows (*Configurações → Personalização → Barra de tarefas → Comportamentos → Alinhamento*):
- **Centralizado**: um player em cada monitor, no canto esquerdo da barra.
- **À esquerda**: o player fica colado à esquerda da bandeja do sistema (perto do relógio) em cada monitor.

- **Barra na lateral** (esquerda/direita, via ExplorerPatcher, StartAllBack etc.): o player vira uma coluna compacta (botões, capa e baterias empilhados), colada no topo da barra ou acima da bandeja.

Mudou o alinhamento ou a posição da barra? O player se ajusta sozinho.

## Observações
- O menu segue o idioma do Windows: português, inglês, espanhol, francês ou alemão (outros idiomas usam inglês).
- "Iniciar com o Windows" usa a lista de inicialização do Windows (também aparece no Gerenciador de Tarefas › Aplicativos de inicialização). Se você mover o exe de pasta, basta abri-lo uma vez para o caminho ser atualizado.
- Todo dispositivo Bluetooth conectado (fone, caixa de som, controle, teclado, mouse) aparece com seu ícone. Se o próprio aparelho não informar a bateria ao Windows, ele aparece com **--** no lugar da porcentagem.
- Alguns aparelhos só informam a bateria em faixas (o DualSense, por exemplo, informa 11 níveis), então a % segue a precisão do próprio aparelho.
- Mouses e teclados com dongle de protocolo próprio e não documentado podem não exibir bateria.
- As configurações ficam em `%AppData%\MiniPlayer.txt`.

## Compilar o código
Requer o .NET 8 SDK. Rode `Compilar.bat` ou:
```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```
