<p align="center"><img src="docs/icon.png" width="96" alt="MiniPlayer"></p>
<h1 align="center">MiniPlayer</h1>
<p align="center"><a href="README.md">English</a> · <b>Português</b></p>
<p align="center">Mini player de mídia para a barra de tarefas do Windows 10/11 — com espectro de áudio, volume na rolagem do mouse, a bateria dos seus fones, controles, mouses e teclados, e um monitor do sistema compacto.</p>
<p align="center"><a href="../../releases/latest"><b>⬇ Baixar a versão mais recente</b></a></p>

<p align="center"><img src="docs/horizontal.svg" alt="MiniPlayer na barra de tarefas"></p>

> As imagens desta página são ilustrações do layout do app.

## Download
Em [Releases](../../releases/latest) há duas opções:

| Arquivo | Para quem |
|---|---|
| **MiniPlayer-Setup.exe** *(recomendado)* | Instala só para o seu usuário (sem pedir administrador), cria atalho no menu Iniciar e já deixa "Iniciar com o Windows" ativado. Atualizar = baixar e instalar a versão nova por cima. |
| **MiniPlayer.exe** *(portátil)* | Não instala nada: é só abrir, de qualquer pasta. |

> Na primeira execução o Windows SmartScreen pode avisar sobre "editor desconhecido": clique em **Mais informações → Executar assim mesmo**.

## O que ele faz

### 🎵 Mídia
- **Controla qualquer player**: voltar, play/pause e avançar, com capa, título e artista do que estiver tocando (Spotify, Tidal, YouTube e outros sites no navegador, players de vídeo…).
- **Barra de progresso** abaixo do título — clique nela para avançar ou voltar.
- **Título longo rola** ao passar o mouse sobre ele.
- **Volume na rolagem do mouse**: role sobre a capa ou os botões para mudar o volume do app que está tocando. Passe o mouse na capa para ver o volume atual.
- **Várias mídias abertas?** O player mostra a que está tocando com som. Para trocar manualmente, role o mouse sobre o título ou clique no botão **⇅** (aparece só quando há mais de uma) — a mídia gira para cima/baixo com uma animação curta.
- **Prévias do YouTube** ao passar o mouse nas miniaturas são ignoradas.
- **Streamings sem capa** (Netflix, Prime Video, Disney+…) ganham uma imagem padrão; sem nenhuma informação, aparece uma nota musical.
- **Espectro de áudio** discreto atrás dos botões, na cor da capa.

### 🔋 Bateria dos dispositivos
Em tempo real, com ícone por tipo e um ⚡ quando está carregando. Passe o mouse sobre um dispositivo para ver o nome e o **tempo restante estimado**; clique para abrir as configurações de Bluetooth do Windows.
- Tudo que o Windows informa via Bluetooth: fones, caixas de som, controles Xbox, teclados, mouses…
- DualSense e DualShock 4 (Bluetooth ou cabo), lidos direto do controle.
- Logitech (Lightspeed, Unifying e Bolt), Razer sem fio, Nintendo Pro Controller e Joy-Con.
- Mouses e teclados 2.4 GHz que seguem o padrão HID de bateria.
- Aparelhos que não informam bateria aparecem com **--**.

### 📊 Monitor do sistema
Escolha os itens: **RAM, CPU, temperatura da CPU, GPU, temperatura da GPU, velocidade da rede** (com ícone de Wi-Fi ou cabo) e **ping**.
- Mostra 3 itens por vez — **role o mouse** sobre eles (ou clique nas setas) para ver os outros, ou ligue a rolagem automática. Ou desligue o limite para ver todos de uma vez.
- **Arraste** um item para mudar a ordem. **Clique** para abrir o Gerenciador de Tarefas.
- Os valores ficam laranja quando altos (uso ≥ 90%, temperatura ≥ 85 °C, ping ≥ 150 ms).
- Funciona em qualquer hardware, sem administrador nem drivers. A temperatura da CPU depende da placa-mãe informá-la ao Windows.

### 🖥️ Na tela
- **Encaixa no espaço livre** da barra de tarefas, em todos os monitores — sem cobrir os ícones da barra. Se faltar espaço, encolhe por etapas (menos itens do monitor, depois sem título, depois sem capa). Funciona também com a **barra na lateral**:

<p align="center"><img src="docs/vertical.svg" alt="MiniPlayer em barra de tarefas vertical"></p>

- **Solto na tela**: arraste para onde quiser, na horizontal ou na vertical, acima ou atrás das janelas — e trave a posição se quiser.
- **Some sozinho** em jogos e vídeos em tela cheia.
- **Nítido** em qualquer escala do Windows (125%, 150%, 200%…).
- **Leve**: só faz trabalho quando algo muda.

### 🎨 Temas
11 temas com degradê, incluindo o **Dinâmico**, que acompanha as cores da capa que está tocando:

<p align="center"><img src="docs/temas.svg" alt="Temas do MiniPlayer"></p>

## Menu
Clique com o botão direito no player — ou no ícone do MiniPlayer na bandeja do sistema, perto do relógio (se não aparecer, procure na seta **^**). Os dois menus têm as mesmas opções.

| Opção | O que faz |
|---|---|
| **Fixo na barra de tarefas** | ✓ = encaixado na barra (padrão). Desmarcado = solto: arraste para onde quiser; ele sempre fica inteiro dentro de um monitor. |
| **Travar posição** | ✓ = o player solto não pode ser arrastado (útil com outros apps de barra de tarefas). Só no modo solto. |
| **Posição (Solto)** | Layout vertical ou horizontal, e **Atrás das janelas** (funciona como widget da área de trabalho; continua visível sobre a barra e no **Win+D**). Só no modo solto. |
| **Exibir** | Controles de mídia, bateria dos dispositivos e monitor do sistema (com seus itens e a rolagem automática). Um grupo sempre fica visível. |
| **Tema** | Padrão, Escuro, Claro, Neon, Pôr do sol, Oceano, Floresta, Púrpura, Rubi, Alto contraste e Dinâmico. |
| **Idioma** | Automático (segue o Windows) ou Português, English, Español, Français, Deutsch. |
| **Tamanho** | Pequeno / Médio / Grande. |
| **Iniciar com o Windows** | ✓ = abre o MiniPlayer ao ligar o PC. |
| **Encerrar MiniPlayer** | Fecha o app. |

## Onde ele fica na barra
O encaixe segue o alinhamento da barra do Windows (*Configurações → Personalização → Barra de tarefas → Comportamentos → Alinhamento*):
- **Centralizado**: um player em cada monitor, no lado esquerdo da barra (depois do botão de Widgets).
- **À esquerda**: ao lado da bandeja/relógio, em cada monitor.
- **Barra na lateral** (esquerda/direita, via ExplorerPatcher, StartAllBack etc.): coluna compacta no topo da barra ou acima da bandeja.

Mudou o alinhamento ou a posição da barra? O player se ajusta sozinho.

## Dúvidas comuns
- **O player mostra o vídeo em que passei o mouse no YouTube.** O app já ignora prévias sem som. Para eliminar de vez: no YouTube, foto de perfil → **Configurações → Reprodução e desempenho** → desmarque **Reprodução inline**.
- **A bateria do meu aparelho aparece com "--".** O próprio aparelho não informa a bateria ao Windows. Mouses/teclados com dongle de protocolo próprio também podem não informar.
- **A % do controle muda de 10 em 10.** Alguns aparelhos só informam a bateria em faixas (o DualSense informa 11 níveis); o app mostra a precisão que o aparelho entrega.
- **O volume da rolagem muda o volume geral, não o do app.** Acontece quando o app não aparece no mixer de volume do Windows com um nome reconhecível (ex.: Firefox).
- **"Iniciar com o Windows" não funcionou.** Ele usa a lista de inicialização do Windows (Gerenciador de Tarefas › Aplicativos de inicialização) — confira se não está desativado lá. Se moveu o exe portátil de pasta, abra-o uma vez para o caminho ser atualizado.

## Privacidade
O MiniPlayer **não coleta dados** e não tem conta, telemetria ou anúncios. Título, capa, áudio (para o espectro), bateria e informações do sistema são lidos e usados só na memória do seu PC, na hora. As únicas coisas salvas são suas preferências, em `%AppData%\MiniPlayer.txt`.
O único tráfego de rede é o item opcional **Ping**: enquanto ligado, envia um pacote minúsculo (16 bytes) a cada 2 segundos para 1.1.1.1 (Cloudflare) ou 8.8.8.8 (Google) para medir a latência. A velocidade da rede só lê os contadores do próprio Windows e não envia nada.

## Compilar o código
Requer o .NET 8 SDK. Rode `Compilar.bat` ou:
```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```
O instalador é gerado com [Inno Setup](https://jrsoftware.org/isinfo.php) a partir de `installer.iss`.
