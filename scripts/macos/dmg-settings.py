# dmgbuild settings for the Wandur disk image, used by scripts/macos/make-dmg.sh.
#
# dmgbuild writes the window's layout (.DS_Store) itself, without driving Finder through AppleScript, so the
# image looks the same whether it is built on a CI runner or a Mac. It copies the app with ditto, which keeps
# the signatures of the managed .dll files that live in extended attributes.
#
# The layout matches scripts/macos/dmg-background.py: a 640 by 400 point window, 128 point icons centred at
# (160, 196) for the app and (480, 196) for the Applications shortcut. Change both together.
import os.path

app = defines["app"]  # noqa: F821 (dmgbuild provides defines)
application = os.path.basename(app)

format = "UDZO"
filesystem = "HFS+"
files = [app]
symlinks = {"Applications": "/Applications"}
icon = defines.get("volume_icon")  # noqa: F821
background = defines["background"]  # noqa: F821

window_rect = ((200, 140), (640, 400))
default_view = "icon-view"
show_status_bar = False
show_tab_view = False
show_toolbar = False
show_pathbar = False
show_sidebar = False
include_icon_view_settings = True

icon_size = 128
text_size = 13
arrange_by = None
icon_locations = {
    application: (160, 196),
    "Applications": (480, 196),
}
