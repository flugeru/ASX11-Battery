# ASX11 Battery

Monitor de bateria em tempo real para o mouse **Attack Shark X11** (com ou sem fio), no Windows 10 e 11.

## O que ele faz

ASX11 Battery lê diretamente o relatório HID emitido pelo receptor USB do X11 e exibe a **porcentagem real da bateria**. Não estima valores — quando a leitura não é possível, mostra **"Bateria indisponível"**.

## Principais funcionalidades

- **Leitura real por HID**: interpreta os relatórios passivos de 5 bytes (`03 55 40 01 XX` normal, `03 55 40 03 XX` carregando) enviados pelo receptor.
- **Suporte com/sem fio**: detecta automaticamente a conexão e indica se está carregando via cabo.
- **Ícone na bandeja (System Tray)**: continua monitorando quando a janela é fechada; abre, acessa configurações ou sai pela bandeja.
- **Tema escuro**: interface fixa em azul-marinho escuro, com contraste alto e foco na leitura.
- **Iniciar com o Windows**: opcional, via chave `Run` do usuário (sem serviço, sem administrador).
- **Notificações**: alerta de bateria baixa e quando inicia/finaliza o carregamento.
- **Diagnóstico integrado**: painel com informações úteis para depuração.
- **Leitura apenas**: não envia dados ao dispositivo. O app abre as coleções HID apenas para leitura.

## Requisitos

- Windows 10 (21H2+) ou Windows 11
- .NET 10 SDK 10.0.401 (para compilar; a publicação self-contained não exige runtime instalado)
- Mouse Attack Shark X11 com receptor USB compatível

## Como executar

### Versão pré-compilada

Baixe a pasta publicada (`artifacts\publish` após rodar o build) e execute `ASX11Battery.App.exe`.

### Compilar a partir do código

```powershell
.\build.ps1
```

Isso restaura, compila, executa os testes e publica uma versão **self-contained** para `win-x64` em `artifacts\publish\`.

Opções úteis:

```powershell
.\build.ps1 -Configuration Debug    # Build de Debug
.\build.ps1 -NoTests                # Pula testes
.\build.ps1 -Runtime win-arm64      # Para Windows ARM64
```

Também é possível compilar no Visual Studio/Rider com o SDK .NET 10.0.401 instalado.

## Build no GitHub Actions

O repositório inclui workflow em `.github/workflows/build.yml`. O build exige runner `windows-latest`.

```powershell
.\build.ps1
```

## Estrutura do projeto

O projeto é pequeno e organizado em camadas simples:

- **`src/ASX11Battery.Core`** – Lógica de leitura HID, providers, diagnósticos e serviços de monitoramento (sem dependência de WPF).
- **`src/ASX11Battery.App`** – Interface WPF, temas, controles, viewmodels, tray e ciclo de vida da aplicação.
- **`tests/ASX11Battery.Tests`** – Testes automatizados (xUnit).
- **`tools/`** – Utilitários de apoio para inspeção e geração de assets.
- **`docs/`** – Notas de protocolo e assets de documentação.

## Sobre

Feito por Flügel. Discord: `.flugel.`.

## Licença

Distribuído sob a licença MIT. Veja `LICENSE` para detalhes.