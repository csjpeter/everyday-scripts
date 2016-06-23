#!/bin/bash
if sudo smartctl -a /dev/sda | grep "^Device Model:" | grep "Samsung SSD 850 EVO 1TB"; then
	sudo smartctl -A /dev/sda | grep "^241"
	sudo smartctl -A /dev/sda | awk '/^241/ { print "TBW: "($10 * 512) * 1.0e-12, "TB" }'
elif sudo smartctl -a /dev/sda | grep "^Device Model:" | grep "KINGSTON SKC400S371T"; then
	sudo smartctl -A /dev/sda | grep "^241"
	sudo smartctl -A /dev/sda | awk '/^241/ { print "TBW: "$10 * 1.0e-3, "TB" }'
fi

