#!/usr/bin/env bash

parse_arguments() {
    USE_TOOLS=false
    for arg in "$@"; do
        if [ "$arg" == "-t" ]; then
            USE_TOOLS=true
        fi
    done
}

is_process_running() {
    pgrep -f "$1" > /dev/null
}

get_process_pid() {
    pgrep -f "$1"
}

start_client() {
    local niceness=$1

    echo "Starting Client."
    nice -n "$niceness" dotnet run --project Content.Client

    # Grace period.
    sleep 15

}
# for my weak ass cpu. launching both at the same time kicks my PC's ass.
cpu_delay() {
    local pid=$1
    local num_cores=$2
    local name=$3
    local cpu

    # Adjust CPU threshold based on number of cores
    # On multi-core systems, per-process CPU can exceed 100%
    local max_cpu=$((100 * 2 / num_cores))  # Allow up to 2 cores worth
    local stable_count=0
    local required_stable=3

    while [ $stable_count -lt $required_stable ]; do
        if ps -p "$pid" > /dev/null; then
            cpu=$(ps -p "$pid" -o %cpu= | awk '{print int($1)}')
            if [ "$cpu" -lt $max_cpu ]; then
                stable_count=$((stable_count + 1))
            else
                stable_count=0
            fi
            sleep 3
        else
            echo "Error: $name process died unexpectedly"
            exit 1
        fi
    done

    echo "$name started successfully (PID: $pid)"
}

start_server() {
    local niceness=$1
    local use_tools=$2

    if [ "$use_tools" = true ]; then
        echo "Starting Server in --Tools configuration."
        nice -n "$niceness" dotnet run --project Content.Server --configuration Tools
    else
        echo "Starting Server."
        nice -n "$niceness" dotnet run --project Content.Server
    fi
}
cleanup() {
    local pid

    for pid in "$@"; do
        if [[ "$pid" =~ ^[0-9]+$ ]]; then
            if kill -0 "$pid" 2>/dev/null; then
                echo "Killing process $pid..."
                kill "$pid" 2>/dev/null || kill -9 "$pid" 2>/dev/null
            else
                echo "Process $pid does not exist or already terminated"
            fi
        else
            echo "Warning: '$pid' is not a valid PID"
        fi
    done
}

run() {
    NUM_CORES=$(nproc)
    echo "Detected $NUM_CORES CPU cores"

    # how nice to feel depending on cpu cores
    if [ "$NUM_CORES" -le 2 ]; then
        NICENESS=15  # Be very nice on low-core systems
    elif [ "$NUM_CORES" -le 4 ]; then
        NICENESS=10  # Moderate niceness
    else
        NICENESS=5   # Light niceness on high-core systems
    fi

    # Check if server and client are already running
    SERVER_PID=$(get_process_pid "dotnet run.*Content.Server")
    CLIENT_PID=$(get_process_pid "dotnet run.*Content.Client")

    if [[ ! -z "$CLIENT_PID" && ! -z "$SERVER_PID" ]]; then
        echo "Client and Server are already running."
    elif [ ! -z "$CLIENT_PID" ]; then
        echo "Client is already running (PID: $CLIENT_PID)"
        start_server "$NICENESS" "$USE_TOOLS" &
        SERVER_PID=$(get_process_pid "dotnet run.*Content.Server")
    elif [ ! -z "$SERVER_PID" ]; then
        echo "Server is already running (PID: $SERVER_PID)"
        start_client "$NICENESS" &
        CLIENT_PID=$(get_process_pid "dotnet run.*Content.Client")
    else
        start_client "$NICENESS" &
        CLIENT_PID=$(get_process_pid "dotnet run.*Content.Client")
        cpu_delay "$CLIENT_PID" "$NUM_CORES" "Client"
        start_server "$NICENESS" "$USE_TOOLS" &
        SERVER_PID=$(get_process_pid "dotnet run.*Content.Server")
    fi

    trap 'cleanup $CLIENT_PID $SERVER_PID' EXIT
}

main() {
    parse_arguments "$@"

    run

    while true; do
        echo "Type 'exit' to stop related processes."
        echo "Type 'run' to run any missing processes."
        read -r user_input

        if [[ "$user_input" == "exit" ]]; then
            break
        elif [[ "$user_input" == "run" ]]; then
            run
        fi
    done
    echo "Bye."
    exit 0
}

# Execute main function with all arguments
main "$@"
