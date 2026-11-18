# CloudDesk

Сервис заявок технической поддержки: ASP.NET Core Minimal API + PostgreSQL + Docker Compose.

## Запуск приложения

```bash
cp .env.example .env
docker compose up -d --build
docker compose ps
```

После запуска API доступен на порту `8080` (в Codespaces открыть вкладку **Ports** и нажать на порт 8080).

Проверка:

```bash
curl -i http://localhost:8080/health
curl http://localhost:8080/api/tickets
curl http://localhost:8080/api/stats
```

Остановка:

```bash
docker compose down
```

Полный сброс базы (удаляет данные):

```bash
docker compose down -v
```

## HTTP-маршруты

| Метод | Маршрут | Описание |
|---|---|---|
| GET | `/health` | 200, если API работает и база доступна; 503, если базы нет |
| GET | `/api/tickets` | Список заявок. Параметры: `status`, `priority`, `q`, `sort` (`asc`/`desc`), `page`, `pageSize` |
| GET | `/api/tickets/{id}` | Одна заявка или 404 |
| POST | `/api/tickets` | Создание заявки, ответ 201 и ID |
| PUT | `/api/tickets/{id}` | Изменение заявки, 204 или 404 |
| DELETE | `/api/tickets/{id}` | Удаление, 204 или 404 |
| GET | `/api/stats` | Всего заявок и количество по статусам |
| GET | `/api/tickets/{id}/events` | История смены статусов заявки |

Примеры:

```bash
curl -i -X POST http://localhost:8080/api/tickets \
  -H "Content-Type: application/json" \
  -d '{"title":"Не включается монитор","description":"Монитор не реагирует на кнопку","priority":2}'

curl -i -X PUT http://localhost:8080/api/tickets/1 \
  -H "Content-Type: application/json" \
  -d '{"title":"Не включается монитор","description":"Монитор не реагирует на кнопку","priority":2,"status":"in_progress"}'

curl -i -X DELETE http://localhost:8080/api/tickets/2

curl "http://localhost:8080/api/tickets?status=new&priority=1&q=принтер&sort=asc&page=1&pageSize=5"
```

Ошибки валидации возвращают 400: `title` от 3 до 120 символов, `priority` от 1 до 3, `status` только `new`, `in_progress`, `done`.

## Проверка сохранности данных

1. Создать заявку и запомнить её ID:

   ```bash
   curl -X POST http://localhost:8080/api/tickets -H "Content-Type: application/json" \
     -d '{"title":"Проверка сохранности","description":"Тестовая заявка","priority":1}'
   ```

2. Перезапустить контейнеры без удаления тома и проверить, что заявка на месте:

   ```bash
   docker compose down
   docker compose up -d
   curl http://localhost:8080/api/tickets/7
   ```

3. Остановить только базу и посмотреть на `/health` (должен вернуть 503), затем поднять её снова:

   ```bash
   docker compose stop db
   curl -i http://localhost:8080/health
   docker compose start db
   curl -i http://localhost:8080/health
   ```

4. Пересобрать только API и убедиться, что данные не пропали:

   ```bash
   docker compose up -d --build api
   curl http://localhost:8080/api/stats
   ```

Данные PostgreSQL хранятся в именованном томе `pgdata`, поэтому `docker compose down` их не удаляет. Удаляет только `docker compose down -v`.
