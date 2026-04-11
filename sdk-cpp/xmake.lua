add_rules("mode.debug", "mode.release")
add_requires("ixwebsocket", "nlohmann_json")

target("agent")
    set_kind("binary")
    set_languages("c++17")
    add_files("src/*.cpp")
    add_packages("ixwebsocket", "nlohmann_json")

    after_build(function (target)
        os.cp(
            target:targetfile(),
            path.join(os.projectdir(), "bin", path.filename(target:targetfile()))
        )
    end)
