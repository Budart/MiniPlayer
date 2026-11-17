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

| Opção | O que faz |
|---|---|
| **Soltar (arrastar livremente)** | Desprende o player da barra: arraste-o para qualquer lugar da tela. A posição é lembrada. Desmarque para fixá-lo de volta na barra de tarefas. |
| **Manter atrás das janelas** | Só no modo solto. Marcado: o player fica atrás das janelas, como um widget na área de trabalho. Desmarcado: fica acima de todas as janelas. Ao usar **Win+D** (mostrar a área de trabalho) ou sobre a barra de tarefas, ele continua visível. |
| **Mostrar bateria** | Mostra ou esconde os indicadores de bateria. |
| **Iniciar com o Windows** | Abre o MiniPlayer automaticamente ao ligar o PC. |
| **Fechar** | Encerra o MiniPlayer. |

## Posição na barra de tarefas (modo fixo)
O encaixe segue o alinhamento da barra do Windows (*Configurações → Personalização → Barra de tarefas → Comportamentos → Alinhamento*):
- **Centralizado**: um player em cada monitor, no canto esquerdo da barra.
- **À esquerda**: o player fica colado à esquerda da bandeja do sistema (perto do relógio) em cada monitor.

Mudou o alinhamento? O player se ajusta sozinho.

## Observações
- Alguns aparelhos só informam a bateria em faixas (o DualSense, por exemplo, informa 11 níveis), então a % segue a precisão do próprio aparelho.
- Mouses e teclados com dongle de protocolo próprio e não documentado podem não exibir bateria.
- As configurações ficam em `%AppData%\MiniPlayer.txt`.

## Compilar o código
Requer o .NET 8 SDK. Rode `Compilar.bat` ou:
```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```
