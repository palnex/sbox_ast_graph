# help/wsl_init.sh
# Ініціалізація терміналу в корені проєкту sbox_ast_graph
source ~/.bashrc
cd /mnt/c/Users/yenro/Desktop/sbox_ast_graph

# Розумний пошук та активація віртуального оточення
if [ -f ~/sbox_ast_graph/.venv_wsl_onnx/bin/activate ]; then
    source ~/sbox_ast_graph/.venv_wsl_onnx/bin/activate
elif [ -f .venv_wsl_onnx/bin/activate ]; then
    source .venv_wsl_onnx/bin/activate
fi

clear
python3 help/print_guide.py