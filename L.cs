using System.Collections.Generic;
using System.Globalization;

namespace MiniPlayer
{
    // Textos da interface no idioma do Windows. Idiomas: pt, en, es, fr, de. Qualquer outro usa inglês.
    static class L
    {
        static readonly string[] Langs = { "pt", "en", "es", "fr", "de" };
        static readonly int Lang = System.Array.IndexOf(Langs, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) is var i && i >= 0 ? i : 1;

        static readonly Dictionary<string, string[]> T = new()
        {
            //                  pt                                         en                                       es                                          fr                                           de
            ["pos"]      = new[] { "Posição", "Position", "Posición", "Position", "Position" },
            ["fixed"]    = new[] { "Fixo na barra de tarefas", "Docked to the taskbar", "Fijo en la barra de tareas", "Ancré à la barre des tâches", "An der Taskleiste angedockt" },
            ["float"]    = new[] { "Solto na tela (arrastar)", "Floating (drag anywhere)", "Flotante (arrastrar)", "Flottant (déplaçable)", "Frei schwebend (verschiebbar)" },
            ["vert"]     = new[] { "Layout vertical (solto)", "Vertical layout (floating)", "Diseño vertical (flotante)", "Disposition verticale (flottant)", "Vertikales Layout (schwebend)" },
            ["behind"]   = new[] { "Atrás das janelas (solto)", "Behind windows (floating)", "Detrás de las ventanas (flotante)", "Derrière les fenêtres (flottant)", "Hinter Fenstern (schwebend)" },
            ["lock"]     = new[] { "Travar posição (solto)", "Lock position (floating)", "Bloquear posición (flotante)", "Verrouiller la position (flottant)", "Position sperren (schwebend)" },
            ["size"]     = new[] { "Tamanho", "Size", "Tamaño", "Taille", "Größe" },
            ["small"]    = new[] { "Pequeno", "Small", "Pequeño", "Petit", "Klein" },
            ["medium"]   = new[] { "Médio", "Medium", "Mediano", "Moyen", "Mittel" },
            ["large"]    = new[] { "Grande", "Large", "Grande", "Grand", "Groß" },
            ["left"]     = new[] { "restantes", "left", "restantes", "restantes", "übrig" },
            ["estimating"] = new[] { "calculando o tempo restante…", "estimating time left…", "calculando el tiempo restante…", "estimation du temps restant…", "Restzeit wird berechnet…" },
            ["source"]   = new[] { "Trocar fonte de mídia", "Switch media source", "Cambiar fuente multimedia", "Changer de source", "Medienquelle wechseln" },
            ["volume"]   = new[] { "Role o mouse para o volume", "Scroll to change volume", "Gira la rueda para el volumen", "Molette pour le volume", "Mausrad für Lautstärke" },
            ["system"]   = new[] { "Monitor do sistema", "System monitor", "Monitor del sistema", "Moniteur système", "Systemmonitor" },
            ["sysshow"]  = new[] { "Mostrar monitor", "Show monitor", "Mostrar monitor", "Afficher le moniteur", "Monitor anzeigen" },
            ["ram"]      = new[] { "Uso de RAM", "RAM usage", "Uso de RAM", "Utilisation de la RAM", "RAM-Auslastung" },
            ["cpu"]      = new[] { "Uso de CPU", "CPU usage", "Uso de CPU", "Utilisation du CPU", "CPU-Auslastung" },
            ["gpu"]      = new[] { "Uso de GPU", "GPU usage", "Uso de GPU", "Utilisation du GPU", "GPU-Auslastung" },
            ["cputemp"]  = new[] { "Temperatura da CPU", "CPU temperature", "Temperatura de la CPU", "Température du CPU", "CPU-Temperatur" },
            ["gputemp"]  = new[] { "Temperatura da GPU", "GPU temperature", "Temperatura de la GPU", "Température du GPU", "GPU-Temperatur" },
            ["na"]       = new[] { "indisponível neste PC", "not available on this PC", "no disponible en este PC", "indisponible sur ce PC", "auf diesem PC nicht verfügbar" },
            ["show"]     = new[] { "Exibir", "Show", "Mostrar", "Afficher", "Anzeigen" },
            ["media"]    = new[] { "Controles de mídia", "Media controls", "Controles multimedia", "Commandes multimédia", "Mediensteuerung" },
            ["battery"]  = new[] { "Bateria dos dispositivos", "Device battery", "Batería de los dispositivos", "Batterie des appareils", "Gerätebatterie" },
            ["theme"]    = new[] { "Tema", "Theme", "Tema", "Thème", "Design" },
            ["startup"]  = new[] { "Iniciar com o Windows", "Start with Windows", "Iniciar con Windows", "Lancer avec Windows", "Mit Windows starten" },
            ["quit"]     = new[] { "Encerrar MiniPlayer", "Exit MiniPlayer", "Salir de MiniPlayer", "Quitter MiniPlayer", "MiniPlayer beenden" },
            ["nothing"]  = new[] { "Nada tocando", "Nothing playing", "Nada en reproducción", "Aucune lecture", "Keine Wiedergabe" },
            ["untitled"] = new[] { "Sem título", "Untitled", "Sin título", "Sans titre", "Ohne Titel" },
            ["charging"] = new[] { "carregando", "charging", "cargando", "en charge", "wird geladen" },
            ["nobatt"]   = new[] { "conectado (o aparelho não informa a bateria)", "connected (the device doesn't report its battery)", "conectado (el dispositivo no informa la batería)", "connecté (l'appareil n'indique pas sa batterie)", "verbunden (Gerät meldet keinen Akkustand)" },
            // temas, na ordem de App.Themes
            ["t0"] = new[] { "Padrão (transparente)", "Default (transparent)", "Predeterminado (transparente)", "Par défaut (transparent)", "Standard (transparent)" },
            ["t1"] = new[] { "Escuro", "Dark", "Oscuro", "Sombre", "Dunkel" },
            ["t2"] = new[] { "Claro", "Light", "Claro", "Clair", "Hell" },
            ["t3"] = new[] { "Neon", "Neon", "Neón", "Néon", "Neon" },
            ["t4"] = new[] { "Pôr do sol", "Sunset", "Atardecer", "Coucher de soleil", "Sonnenuntergang" },
            ["t5"] = new[] { "Oceano", "Ocean", "Océano", "Océan", "Ozean" },
            ["t6"] = new[] { "Floresta", "Forest", "Bosque", "Forêt", "Wald" },
            ["t7"] = new[] { "Púrpura", "Purple", "Púrpura", "Pourpre", "Purpur" },
            ["t8"] = new[] { "Rubi", "Ruby", "Rubí", "Rubis", "Rubin" },
            ["t9"] = new[] { "Alto contraste", "High contrast", "Alto contraste", "Contraste élevé", "Hoher Kontrast" },
            ["t10"] = new[] { "Dinâmico (cores da capa)", "Dynamic (album colors)", "Dinámico (colores del álbum)", "Dynamique (couleurs de l'album)", "Dynamisch (Albumfarben)" },
        };

        public static string S(string key) => T.TryGetValue(key, out var v) ? v[Lang] : key;
    }
}
