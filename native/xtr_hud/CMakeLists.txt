cmake_minimum_required(VERSION 3.20)
project(xtr_hud LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# Build xtr_hud as a shared DLL with .asi extension
add_library(xtr_hud SHARED
    dllmain.cpp
    hud_renderer.cpp
    hud_renderer.h
    xtr_hud_ipc.h
    xtr_telemetry.h
)

set_target_properties(xtr_hud PROPERTIES
    PREFIX ""
    SUFFIX ".asi"
    OUTPUT_NAME "xtr_hud"
)

target_compile_definitions(xtr_hud PRIVATE
    WIN32_LEAN_AND_MEAN
    NOMINMAX
    _CRT_SECURE_NO_WARNINGS
)

target_link_libraries(xtr_hud PRIVATE
    kernel32
    user32
)

# Static CRT to eliminate external runtime dependencies
if(MSVC)
    set_property(TARGET xtr_hud PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
    target_compile_options(xtr_hud PRIVATE /W4 /WX /O2)
else()
    target_link_options(xtr_hud PRIVATE -static -static-libgcc -static-libstdc++ -s)
endif()
