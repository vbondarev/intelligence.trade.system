# Изолированная HTTPS-проверка BFF session cookie

`.github/scripts/https-security-e2e.sh` создаёт отдельный Compose project, временный сертификат и тестовые секреты, выполняет вход через реальный Identity и удаляет только созданные им контейнеры и том. Требуются Docker Compose с поддержкой `!override`, Node, OpenSSL, curl и установленные браузеры Playwright. Запуск из корня репозитория: `bash .github/scripts/https-security-e2e.sh`. Windows: Git Bash с Docker Desktop и установленными браузерами Playwright. Порты 18082, 18443, 18994 и 18995, сеть `172.29.227.0/24` и имя `issue225securityci` должны быть свободны; домены `localtest.me` и `127.0.0.1.sslip.io` должны разрешаться в loopback.

Тестовые HTTPS-прокси работают только на loopback. Они используют самоподписанный сертификат и нормализуют схему redirect Identity; настройки приложения и production TLS не изменяются. Проверяются реальные атрибуты Set-Cookie BFF, получение session и контрольной SameSite=None; Secure cookie при same-site запросе, исключение session cookie из cross-site fetch при наличии контрольной cookie, а также отправка обеих cookies при top-level GET. Значения cookies и тестовые секреты в результаты не записываются. Если контрольная cookie не дошла, проверка завершается ошибкой как неопределённая.

Linux CI запускает оба проекта Playwright: Chromium и mobile WebKit. Обязательный локальный Windows mobile WebKit security gate сохраняется: его ошибка блокирует принятие и merge согласно [утверждённому Amendment](https://github.com/vbondarev/intelligence.trade.system/issues/225#issuecomment-6884893121). Успех Linux не заменяет Windows gate и не подтверждает все критерии WCAG.

Для локального сравнения с Linux можно запустить `SECURITY_BROWSER_DOCKER=1 bash .github/scripts/https-security-e2e.sh`; браузеры выполняются в закреплённом Docker-образе Playwright 1.63.0. Это не является запуском GitHub Actions.

Результат локальной проверки 2026-11-15: Windows Chromium — PASS; Windows mobile WebKit — FAIL, cross-site запрос содержит session и контрольную cookie. Linux Playwright 1.63.0: Chromium и mobile WebKit — PASS (2/2); оба профиля проверяли в том числе наличие контрольной cookie. Security gate Windows остаётся FAIL.

Повтор 2026-11-16 на текущем рабочем дереве: Windows Chromium — PASS; Windows mobile WebKit — FAIL с тем же результатом cross-site запроса (`session=true`, `control=true`). Изолированный Compose project удалён после проверки. Этот результат сохраняет блокировку security gate.

Повтор 2026-11-16 в закреплённом локальном Linux Playwright-контейнере: Chromium и mobile WebKit — 2/2 PASS. Это локальный запуск, а не результат GitHub Actions; Windows security gate остаётся FAIL.

Повтор 2026-11-17 после сборки Identity: Windows Chromium — PASS, Windows mobile WebKit — FAIL. При cross-site fetch наблюдались `session=true` и `control=true`. В общем browser E2E отдельно повторилось `SameSite=None` вместо `Lax`. Временный HTTPS-стенд удалён, Windows security gate остаётся блокирующим.

После Amendment по сроку BFF-сессии добавлена отдельная HTTPS-проверка: POST без CSRF возвращает `400`, успешный POST сохраняет `Secure`, `HttpOnly` и `SameSite=Lax` в `Set-Cookie`. Windows Chromium и mobile WebKit прошли её. Исходный cross-site тест Windows WebKit снова получил `session=true`, `control=true` и остаётся FAIL. Закреплённый Linux Playwright-контейнер локально прошёл оба теста в каждом браузере (4/4).

## Windows WebKit с исправлением из PR #81431

19 ноября 2026 года опубликован и перечитан [Amendment о нативной Windows-сборке](https://github.com/vbondarev/intelligence.trade.system/issues/225#issuecomment-7006785255). Контрольный прогон действующего HTTPS gate на штатной сборке дал 3 PASS и 1 FAIL. В Windows WebKit сервер получил обе cookie при cross-site fetch. Локальный закреплённый Linux-контейнер дал 4 PASS, контрольный realtime recovery на штатных браузерах дал 6 PASS. Для приёмки требуются PASS на исправленной Windows-сборке, полный Windows E2E, повтор realtime recovery на исправленном WebKit и предусмотренный Linux CI.

Исходники для Windows-сборки закреплены на Playwright `1b025d7e20a026371cd5f98ba0cdce48892737c8` (v1.63.0, r2359), WebKit `4d05d732e5a84f32675bef4cc135a2e7a9269a87` и [коммите исправления `1ef2cad8`](https://github.com/WebKit/WebKit/commit/1ef2cad825b2e83009ed474fe7d00a4e60f4659a). В r2359 файл `NetworkStorageSessionCurl.cpp` находится в WebCore. Для переноса изменений требуются адаптация пути, сохранение сигнатур r2359 и согласование с `bootstrap.diff` Playwright. Прохождение проверки применения патча подтверждает подготовку исходников. Статус gates определяется результатами запуска собранного браузера.

По решению владельца эксперимент с локальной сборкой прекращён. Процессы загрузки исходников и подготовки зависимостей остановлены. Скачанные исходники, архивы и локальный перенос патча удалены. Проверка исправленного Windows WebKit после выхода официальной сборки Playwright отслеживается в [Issue #226](https://github.com/vbondarev/intelligence.trade.system/issues/226). До PASS обязательного Windows gate и остальных критериев IMP-07 сохраняется `BLOCKED_BY_VERIFICATION`.

Запущенный с повышенными правами установщик Microsoft Build Tools отклонил попытку остановки из текущего сеанса. Его загрузчик остался заблокирован. Каталоги установленных инструментов сохранены.

При подготовке Draft PR переход верхнего уровня в HTTPS-тесте выполнен щелчком по ссылке на другом сайте. Повтор на штатной Windows-сборке дал 3 PASS и 1 FAIL с `session=true`, `control=true` в cross-site fetch. Локальный Linux-контроль дал 4 PASS. Проверка будущего официального Windows-артефакта ведётся в #226.
