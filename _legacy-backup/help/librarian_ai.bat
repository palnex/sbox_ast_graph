@echo off
chcp 65001 > nul
cls
echo Відкриваємо термінал WSL у корені проєкту...
echo.

:: Видаляємо невидимі символи \r з файлу конфігу, щоб Linux не ламався
wsl sed -i "s/\r$//" /mnt/c/Users/yenro/Desktop/sbox_ast_graph/help/wsl_init.sh

:: Запускаємо WSL
wsl -e bash --rcfile /mnt/c/Users/yenro/Desktop/sbox_ast_graph/help/wsl_init.sh