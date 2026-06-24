#!/usr/bin/env node

const fs = require("fs");
const path = require("path");

const [,, reportPath, expectedSymbol, expectedMode, errorsPath, schemaPathArg] = process.argv;

const schemaPath = schemaPathArg || "/home/node/.openclaw/schemas/technical-report.schema.json";

if (!reportPath || !expectedSymbol || !expectedMode || !errorsPath) {
  console.error("Usage: validate-technical-report.js <reportPath> <expectedSymbol> <expectedMode> <errorsPath> [schemaPath]");
  process.exit(2);
}

const errors = [];

function readJson(filePath, label) {
  try {
    return JSON.parse(fs.readFileSync(filePath, "utf8"));
  } catch (error) {
    throw new Error(`${label} is not valid JSON: ${error.message}`);
  }
}

function isObject(value) {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

function normalizeSchemaPath(schemaPath) {
  if (path.isAbsolute(schemaPath)) {
    return schemaPath;
  }

  return path.resolve(process.cwd(), schemaPath);
}

function resolveRef(schema, ref) {
  if (!ref.startsWith("#/$defs/")) {
    throw new Error(`Unsupported schema $ref: ${ref}`);
  }

  const key = ref.slice("#/$defs/".length);
  const definition = schema.$defs?.[key];

  if (!definition) {
    throw new Error(`Missing schema definition: ${ref}`);
  }

  return definition;
}

function getTypeName(value) {
  if (value === null) return "null";
  if (Array.isArray(value)) return "array";
  return typeof value;
}

function isAllowedType(value, type) {
  const types = Array.isArray(type) ? type : [type];
  const actual = getTypeName(value);
  return types.includes(actual);
}

function formatPath(pathSegments) {
  if (pathSegments.length === 0) return "root";
  return pathSegments.join(".");
}

function validateBySchema(value, schemaNode, rootSchema, pathSegments = []) {
  if (!schemaNode || typeof schemaNode !== "object") {
    return;
  }

  if (schemaNode.$ref) {
    validateBySchema(value, resolveRef(rootSchema, schemaNode.$ref), rootSchema, pathSegments);
    return;
  }

  const currentPath = formatPath(pathSegments);

  if (schemaNode.const !== undefined && value !== schemaNode.const) {
    errors.push(`${currentPath} must be ${JSON.stringify(schemaNode.const)}, got ${JSON.stringify(value)}`);
    return;
  }

  if (schemaNode.enum && !schemaNode.enum.includes(value)) {
    errors.push(`${currentPath} has unsupported value: ${JSON.stringify(value)}`);
    return;
  }

  if (schemaNode.type && !isAllowedType(value, schemaNode.type)) {
    errors.push(`${currentPath} must be ${JSON.stringify(schemaNode.type)}, got ${getTypeName(value)}`);
    return;
  }

  if (typeof value === "string") {
    if (schemaNode.minLength !== undefined && value.length < schemaNode.minLength) {
      errors.push(`${currentPath} length must be >= ${schemaNode.minLength}`);
    }

    if (schemaNode.maxLength !== undefined && value.length > schemaNode.maxLength) {
      errors.push(`${currentPath} length must be <= ${schemaNode.maxLength}`);
    }

    if (schemaNode.pattern) {
      const regex = new RegExp(schemaNode.pattern);
      if (!regex.test(value)) {
        errors.push(`${currentPath} must match pattern ${schemaNode.pattern}`);
      }
    }
  }

  if (isObject(value)) {
    const required = Array.isArray(schemaNode.required) ? schemaNode.required : [];
    for (const key of required) {
      if (!Object.prototype.hasOwnProperty.call(value, key)) {
        errors.push(`${currentPath}.${key} is required`);
      }
    }

    const properties = schemaNode.properties || {};
    for (const [key, propertySchema] of Object.entries(properties)) {
      if (Object.prototype.hasOwnProperty.call(value, key)) {
        validateBySchema(value[key], propertySchema, rootSchema, [...pathSegments, key]);
      }
    }
  }

  if (Array.isArray(value) && schemaNode.items) {
    value.forEach((item, index) => {
      validateBySchema(item, schemaNode.items, rootSchema, [...pathSegments, String(index)]);
    });
  }
}

function validateCrossFieldRules(report) {
  if (!isObject(report)) {
    errors.push("root must be an object");
    return;
  }

  if (report.symbol !== expectedSymbol) {
    errors.push(`root.symbol must match requested symbol ${expectedSymbol}, got ${report.symbol}`);
  }

  if (report.analysis_mode !== expectedMode) {
    errors.push(`root.analysis_mode must match backend mode ${expectedMode}, got ${report.analysis_mode}`);
  }

  if (report.status === "ok" && report.data_quality?.confidence === "low") {
    errors.push("status ok should not have low confidence; use partial when confidence is low");
  }
}

function writeErrorsAndExit(exitCode) {
  const text = errors.length > 0 ? errors.map((error) => `- ${error}`).join("\n") + "\n" : "OK\n";
  fs.writeFileSync(errorsPath, text);

  if (exitCode !== 0) {
    console.error(text);
  }

  process.exit(exitCode);
}

let report;
let schema;

try {
  report = readJson(reportPath, "technical_report");
  schema = readJson(normalizeSchemaPath(schemaPath), "technical_report_schema");
} catch (error) {
  errors.push(error.message);
  writeErrorsAndExit(1);
}

validateBySchema(report, schema, schema);
validateCrossFieldRules(report);

if (errors.length > 0) {
  writeErrorsAndExit(1);
}

writeErrorsAndExit(0);
