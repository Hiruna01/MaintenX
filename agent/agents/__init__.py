"""
One file per agent, one owner each.

Everything specific to an agent — its prompt names, its allowed tool subset, how it reads
its context — belongs in that agent's own file. graph.py stays thin and group-owned so
two people adding two agents do not both edit the same logic.
"""
