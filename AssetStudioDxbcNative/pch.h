// pch.h: This is a precompiled header file.
// Files listed below are compiled only once, improving build performance for future builds.
// This also affects IntelliSense performance, including code completion and many code browsing features.
// However, files listed here are ALL re-compiled if any one of them is updated between builds.
// Do not add files here that you will be updating frequently as this negates the performance advantage.

#ifndef PCH_H
#define PCH_H

// add headers that you want to pre-compile here
#include "d3dcompiler.h"
#include <string>
#include <iostream>
#include <algorithm>
#include <vector>
#include <span>
#include <stdexcept>
#include <unordered_map>
#include <stdio.h>
#include <tchar.h>
#include "D3D_Shaders/includes/log.h"

struct shader_ins
{
	unsigned opcode : 11;
	unsigned _11_23 : 13;
	unsigned length : 7;
	unsigned extended : 1;
};
struct token_operand
{
	unsigned comps_enum : 2; /* sm4_operands_comps */
	unsigned mode : 2; /* sm4_operand_mode */
	unsigned sel : 8;
	unsigned file : 8; /* SM_FILE */
	unsigned num_indices : 2;
	unsigned index0_repr : 3; /* sm4_operand_index_repr */
	unsigned index1_repr : 3; /* sm4_operand_index_repr */
	unsigned index2_repr : 3; /* sm4_operand_index_repr */
	unsigned extended : 1;
};

std::vector<DWORD> assembleIns(std::string s);
std::vector<::byte> readFile(std::string fileName);
std::vector<DWORD> ComputeHash(::byte const* input, DWORD size);
std::vector<std::string> stringToLines(const char* start, std::size_t size);
DWORD strToDWORD(std::string s);
std::string convertF(DWORD original);
void writeLUT();

#endif //PCH_H
