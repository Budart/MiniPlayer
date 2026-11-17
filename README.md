# MiniPlayer

Mini player de mídia que fica encaixado na barra de tarefas do Windows 10/11, com espectro de áudio e bateria dos seus dispositivos.

## Download
Baixe o **MiniPlayer.exe** em [Releases](../../releases/latest). É portátil: não precisa instalar nada, é só abrir.
Clique com o botão direito no player e marque **Iniciar com o Windows** para ele abrir sozinho.

## Recursos
- **Controle de mídia universal**: voltar, play/pause e avançar, com capa, título e artista de qualquer app (Spotify, Tidal, YouTube, navegador...).
- **Espectro de áudio** atrás dos botões, na cor da capa que está tocando.
- **Bateria dos dispositivos** à direita, em tempo real, com ícone por tipo (controle, fone, teclado, mouse...) e raio quando está carregando:
  - Bluetooth: tudo que o Windows informa (fones, controles Xbox, teclados e mouses).
  - DualSense e DualShock 4 (Bluetooth e USB), lidos direto do controle.
  - Logitech (Lightspeed, Unifying, Bolt), Razer sem fio, Nintendo Pro Controller/Joy-Con.
  - Mouses e teclados 2.4 GHz que seguem o padrão HID de bateria.
- **Encaixe automático**: barra centralizada → um player em cada monitor, no canto esquerdo; barra à esquerda → colado na bandeja/relógio.
- Some automaticamente em jogos e vídeos em tela cheia.
- Menu (botão direito): soltar para arrastar livremente, mostrar/ocultar bateria, iniciar com o Windows, fechar.

## Observações
- Alguns aparelhos só informam a bateria em faixas (ex.: DualSense informa 11 níveis), então a % segue essa precisão.
- Dongles com protocolo próprio não documentado podem não exibir bateria.

## Compilar
Requer .NET 8 SDK: rode `Compilar.bat`.
