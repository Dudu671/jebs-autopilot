# Jeb's Autopilot (KSP 1.12.x)

Plugin de piloto automático com **hot reload**: o KSP carrega só o Host; a lógica vive no Core e pode ser recompilada com o jogo aberto.

## Ajustar `KSPDir`

`Directory.Build.props` já aponta para a instalação Steam local:

`C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program`

Mude `KSPDir` se o jogo estiver em outro disco. O build falha com mensagem clara se esse caminho não existir. Dá para sobrescrever na linha de comando:

```bat
dotnet build /p:KSPDir="C:\caminho\do\KSP"
```

## Compilar

```bat
dotnet build KspAutopilot.sln
```

- **AutopilotHost** → `$(KSPDir)/GameData/MyAutopilot/Plugins/`
- **AutopilotCore** → `$(KSPDir)/GameData/MyAutopilot/Dev/` (não vai para `Plugins/`)

## Hot reload

1. Abra o KSP e entre em voo (o Host só roda em `Flight`).
2. Altere o Core e rode `dotnet build` de novo.
3. O Host observa `GameData/MyAutopilot/Dev/AutopilotCore.dll`, espera 0,3 s e recarrega: destrói o `GameObject` antigo, carrega os bytes da DLL (`Assembly.Load` em memória, sem travar o arquivo) e adiciona `AutopilotCore.Controller`.

Não é preciso sair do voo nem reiniciar o jogo para iterar na lógica.
