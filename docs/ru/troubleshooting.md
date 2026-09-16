# Aiko - Решение проблем

## Установщик не сработал

`irm https://raw.githubusercontent.com/jrfrigat/Aiko/main/scripts/install.ps1 | iex` требует:

- 64-битной Windows - релиз собран только для win-x64;
- исходящего HTTPS к `github.com` и `objects.githubusercontent.com`;
- опубликованного релиза. Если релиза ещё нет, скрипт скажет об этом; конкретный тег можно закрепить
  через `-Version v0.1.0` - при неверном теге или отсутствующем ассете в ошибке будет точный URL.

Если политика выполнения блокирует `irm | iex`, запустите явно:

```powershell
powershell -ExecutionPolicy Bypass -Command "irm https://raw.githubusercontent.com/jrfrigat/Aiko/main/scripts/install.ps1 | iex"
```

Команда `aiko` не находится после установки: откройте новый терминал (или проверьте, что
`%LOCALAPPDATA%\Aiko\bin` есть в пользовательском `PATH`).

## Порт занят

Aiko предпочитает порт `24560`. Если он занят, `aiko serve` спрашивает другой порт (или выбирает
свободный из `18000-18999` в неинтерактивном режиме) и сохраняет его. Если порт сменился,
MCP-конфиги агентов могли устареть - переустановите их:

```powershell
aiko agent install --project <projectId>
```

## 401 Unauthorized

Демон требует токен доступа (или cookie сессии) для `/api/v1/*` и `/mcp/*`. При 401:

- Откройте UI через `aiko ui` (он сопрягает браузер).
- Для скриптов/агентов передавайте `Authorization: Bearer <token>`; получите токен через
  `aiko token show`.

`AIKO_INSECURE=1` отключает аутентификацию только для локальной отладки.

## 403 / 400 в браузере

Демон отклоняет не-loopback хосты и удалённые browser origins. Держите всё на
`http://127.0.0.1:<port>`.

## Агент не видит Aiko

1. `aiko agent list` - убедитесь, что агент найден.
2. `aiko agent install --project <projectId>` (и `aiko agent install --scope user`).
3. Перезапустите агента, чтобы он загрузил новый MCP-сервер.
4. Проверьте эндпоинт: `GET http://127.0.0.1:<port>/health` должен вернуть `healthy`.

## 409 Conflict

Карточки и workflow используют optimistic revisions. Если два клиента правят одно и то же, один
получает `409` с ожидаемой и фактической ревизией - перезагрузите и повторите.

## Устаревшая доска или проекции

Доска читает из SQLite-проекций, построенных из `.aiko`. Если вы правили файлы вручную,
перестройте:

```powershell
aiko reindex <projectId>
```

Удаление базы безопасно - `aiko init` (или `POST /api/v1/projects/initialize`) и `aiko reindex`
перестраивают всё из `.aiko`.

## Агент пропустил aiko_get_project_context

Aiko не может заставить агента вызывать инструмент. Установленные скиллы, блок `AGENTS.md` и
правило Cursor закрепляют контракт "сначала контекст"; при необходимости укажите это явно в
промпте.
