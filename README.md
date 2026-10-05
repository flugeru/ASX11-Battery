# ASX11-Battery

**Monitor de bateria para o mouse Attack Shark X11 no Windows.**

> ⚠️ **Beta** — o aplicativo ainda está em desenvolvimento e pode apresentar bugs, especialmente na detecção HID e do carregamento via USB.

## Recursos

* Leitura real da bateria via HID
* Detecção com e sem fio
* Indicação de carregamento
* Monitoramento pela bandeja do sistema
* Inicialização com o Windows
* Notificações de bateria
* Interface simples e escura

## Requisitos

* Windows 10 ou Windows 11
* Attack Shark X11 com receptor USB

## Compilar

Requer o **.NET 10 SDK**.

```powershell
.\build.ps1
```

O executável publicado será gerado em:

```text
artifacts\publish\
```

## Licença

MIT
